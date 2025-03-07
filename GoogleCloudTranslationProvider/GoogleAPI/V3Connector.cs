﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Google.Api.Gax.ResourceNames;
using Google.Cloud.AutoML.V1;
using Google.Cloud.Translate.V3;
using GoogleCloudTranslationProvider.Helpers;
using GoogleCloudTranslationProvider.Interfaces;
using GoogleCloudTranslationProvider.Models;
using NLog;
using Sdl.Core.Globalization;

namespace GoogleCloudTranslationProvider.GoogleAPI
{
	public class V3Connector
	{
		private readonly Logger _logger = LogManager.GetCurrentClassLogger();
		private readonly ITranslationOptions _options;

		private TranslationServiceClient _translationServiceClient;
		private List<V3LanguageModel> _supportedLanguages;

		public V3Connector(ITranslationOptions options)
		{
			_options = options;
			ConnectToApi();
		}

		#region Connection
		private void ConnectToApi()
		{
			try
			{
				Environment.SetEnvironmentVariable(Constants.GoogleApiEnvironmentVariableName, _options.JsonFilePath);
				_translationServiceClient = TranslationServiceClient.Create();
            }
			catch (Exception e)
			{
				_logger.Error($"{MethodBase.GetCurrentMethod().Name}: {e}");
				ErrorHandler.HandleError(e);
			}
		}

		public void TryAuthenticate()
		{
			var sourceCultureInfo = new CultureInfo("en-GB");
			var targetCultureInfo = new CultureInfo("de-DE");
			var request = new TranslateTextRequest
			{
				Contents = { string.Empty },
				SourceLanguageCode = "en",
				TargetLanguageCode = "de",
				ParentAsLocationName = new LocationName(_options.ProjectId, _options.ProjectLocation),
				MimeType = "text/plain",
				Model = SetCustomModel(sourceCultureInfo, targetCultureInfo),
				GlossaryConfig = SetGlossary(sourceCultureInfo, targetCultureInfo)
			};

			_translationServiceClient.TranslateText(request);
		}
		#endregion

		#region Languages
		public bool IsSupportedLanguage(CultureCode sourceCulture, CultureCode targetCulture)
		{
			EnsureSupportedLanguagesInitialized();
			var sourceLanguageCode = sourceCulture.GetLanguageCode(ApiVersion.V3);
			var targetLanguageCode = targetCulture.GetLanguageCode(ApiVersion.V3);
			if (string.IsNullOrEmpty(sourceLanguageCode) || string.IsNullOrEmpty(targetLanguageCode))
			{
				return false;
			}

			var sourceGoogleLanguage = GetSupportedLanguage(sourceLanguageCode);
			var targetGoogleLanguage = GetSupportedLanguage(targetLanguageCode);
			return sourceGoogleLanguage?.SupportSource is true
				&& targetGoogleLanguage?.SupportTarget is true;
		}

		private void EnsureSupportedLanguagesInitialized()
		{
			if (_supportedLanguages is null || !_supportedLanguages.Any())
			{
				SetGoogleAvailableLanguages();
			}
		}

		private V3LanguageModel GetSupportedLanguage(string languageCode)
		{
			return _supportedLanguages.FirstOrDefault(lang => lang.GoogleLanguageCode.Equals(languageCode));
		}

		public List<V3LanguageModel> GetLanguages()
		{
			var locationName = new LocationName(_options.ProjectId, "global");
			var request = new GetSupportedLanguagesRequest { ParentAsLocationName = locationName };
			var response = _translationServiceClient.GetSupportedLanguages(request);
			return response.Languages.Select(language => new V3LanguageModel
			{
				GoogleLanguageCode = language.LanguageCode,
				SupportSource = language.SupportSource,
				SupportTarget = language.SupportTarget,
				CultureInfo = new CultureInfo(language.LanguageCode)
			}).ToList();
		}

		private void SetGoogleAvailableLanguages()
		{
			try
			{
				TrySetGoogleAvailableLanguages();
			}
			catch (Exception e)
			{
				_logger.Error($"{MethodBase.GetCurrentMethod().Name}: {e}");
			}
		}

		private void TrySetGoogleAvailableLanguages()
		{
			_supportedLanguages ??= new();
			var locationName = new LocationName(_options.ProjectId, "global");
			var request = new GetSupportedLanguagesRequest { ParentAsLocationName = locationName };
			var response = _translationServiceClient.GetSupportedLanguages(request);

			_supportedLanguages.AddRange(response.Languages.Select(language => new V3LanguageModel
			{
				GoogleLanguageCode = language.LanguageCode,
				SupportSource = language.SupportSource,
				SupportTarget = language.SupportTarget,
				CultureInfo = new CultureInfo(language.LanguageCode)
			}));
		}
		#endregion

		#region Translation
		public string TranslateText(CultureCode sourceLanguage, CultureCode targetLanguage, string sourceText, string format)
		{
			try
			{
				return TryTranslateText(sourceLanguage, targetLanguage, sourceText, format);
			}
			catch (Exception e)
			{
				_logger.Error($"{MethodBase.GetCurrentMethod().Name}: {e}");
				throw e;
			}
		}

		private string TryTranslateText(CultureCode sourceLanguage, CultureCode targetLanguage, string sourceText, string format)
		{
			var request = CreateTranslationRequest(sourceLanguage, targetLanguage, sourceText, format);
			if (_translationServiceClient.TranslateText(request) is not TranslateTextResponse translationResponse)
			{
				return string.Empty;
			}

			return request.GlossaryConfig is null ? translationResponse.Translations[0].TranslatedText
												  : translationResponse.GlossaryTranslations[0].TranslatedText;
		}

		private TranslateTextRequest CreateTranslationRequest(CultureCode sourceLanguage, CultureCode targetLanguage, string sourceText, string format)
		{
			return new TranslateTextRequest
			{
				Contents = { sourceText },
				SourceLanguageCode = sourceLanguage.GetLanguageCode(ApiVersion.V3),
				TargetLanguageCode = targetLanguage.GetLanguageCode(ApiVersion.V3),
				ParentAsLocationName = new LocationName(_options.ProjectId, _options.ProjectLocation),
				MimeType = format == "text" ? "text/plain" : "text/html",
				Model = SetCustomModel(sourceLanguage, targetLanguage),
				GlossaryConfig = SetGlossary(sourceLanguage, targetLanguage)
			};
		}
		#endregion

		#region Glossaries
		public List<Glossary> GetProjectGlossaries(string location = null)
		{
			var locationName = LocationName.FromProjectLocation(_options.ProjectId, location ?? _options.ProjectLocation);
			var glossariesRequest = new ListGlossariesRequest { ParentAsLocationName = locationName };

            var list = _translationServiceClient.ListGlossaries(glossariesRequest).ToList();
			return list;
		}

		private TranslateTextGlossaryConfig SetGlossary(CultureInfo sourceLanguage, CultureInfo targetLanguage)
		{

			var selectedGlossary = _options.LanguageMappingPairs?
										   .FirstOrDefault(x => x.LanguagePair.SourceCulture.Name == sourceLanguage.Name && x.LanguagePair.TargetCulture.Name == targetLanguage.Name)?
										   .SelectedGlossary
										   .Glossary;
			
			if (selectedGlossary is null
			 || string.IsNullOrEmpty(selectedGlossary.Name))
			{
				return null;
			}

			return new TranslateTextGlossaryConfig
			{
				Glossary = selectedGlossary.Name,
				IgnoreCase = true
			};
		}

        #endregion
        #region AutoML
        public List<Model> GetProjectCustomModels()
		{
            AutoMlClient client = AutoMlClient.Create();
            LocationName parent = new LocationName(_options.ProjectId, _options.ProjectLocation);

            // List models
            var models = client.ListModels(parent);
			//foreach (var model in models)
			//{
			//	Trace.WriteLine($"Model name: {model.Name}");
            //  Trace.WriteLine($"Model id: {model.DatasetId}");
            //  Trace.WriteLine($"Model display name: {model.DisplayName}");
			//}

			return models.ToList();
		}

		private string SetCustomModel(CultureInfo sourceLanguage, CultureInfo targetLanguage)
		{
			var defaultPath = $"projects/{_options.ProjectId}/locations/{_options.ProjectLocation}/models/general/nmt";
			var selectedModel = _options.LanguageMappingPairs?
										   .FirstOrDefault(x => x.LanguagePair.SourceCulture.Name == sourceLanguage.Name && x.LanguagePair.TargetCulture.Name == targetLanguage.Name)?
										   .SelectedModel?
										   .ModelPath;
			return selectedModel switch
			{
				not null => selectedModel,
				_ => defaultPath
			};
		}
		#endregion
	}
}