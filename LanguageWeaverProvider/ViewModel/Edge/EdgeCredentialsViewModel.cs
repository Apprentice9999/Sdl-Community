﻿using System;
using System.Windows.Input;
using LanguageWeaverProvider.Command;
using LanguageWeaverProvider.Extensions;
using LanguageWeaverProvider.Model;
using LanguageWeaverProvider.Model.Interface;
using LanguageWeaverProvider.Services;
using LanguageWeaverProvider.View.Edge;
using LanguageWeaverProvider.ViewModel.Interface;

namespace LanguageWeaverProvider.ViewModel.Edge
{
	public class EdgeCredentialsViewModel : BaseViewModel, ICredentialsViewModel
	{
		private AuthenticationType _authenticationType;

		private string _host;
		private string _apiKey;
		private string _username;
		private string _password;

		public EdgeCredentialsViewModel(ITranslationOptions translationOptions)
		{
			TranslationOptions = translationOptions;
			InitializeCommands();
			LoadCredentials();
		}

		public ITranslationOptions TranslationOptions { get; set; }

		public AuthenticationType AuthenticationType
		{
			get => _authenticationType;
			set
			{
				if (_authenticationType == value) return;
				_authenticationType = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(IsAuthenticationTypeSelected));
				OnPropertyChanged(nameof(IsCredentialsSelected));
				OnPropertyChanged(nameof(IsApiKeySelected));
			}
		}

		public string Host
		{
			get => _host;
			set
			{
				_host = value;
				OnPropertyChanged();
			}
		}

		public string ApiKey
		{
			get => _apiKey;
			set
			{
				_apiKey = value;
				OnPropertyChanged();
			}
		}

		public string Username
		{
			get => _username;
			set
			{
				_username = value;
				OnPropertyChanged();
			}
		}

		public string Password
		{
			get => _password;
			set
			{
				_password = value;
				OnPropertyChanged();
			}
		}

		public bool IsAuthenticationTypeSelected => AuthenticationType != AuthenticationType.None;

		public bool IsCredentialsSelected => AuthenticationType == AuthenticationType.EdgeCredentials;

		public bool IsApiKeySelected => AuthenticationType == AuthenticationType.EdgeApiKey;

		public ICommand BackCommand { get; private set; }

		public ICommand ClearCommand { get; private set; }

		public ICommand SignInCommand { get; private set; }

		public ICommand SelectAuthenticationTypeCommand { get; private set; }

		public event EventHandler StartLoginProcess;

		public event EventHandler StopLoginProcess;

		public event EventHandler CloseRequested;

		public void CloseWindow() => CloseRequested?.Invoke(this, EventArgs.Empty);

		private void InitializeCommands()
		{
			BackCommand = new RelayCommand(Back);
			ClearCommand = new RelayCommand(Clear);
			SignInCommand = new RelayCommand(SignIn);
			SelectAuthenticationTypeCommand = new RelayCommand(SelectAuthenticationType);
		}

		private void LoadCredentials()
		{
			if (TranslationOptions.EdgeCredentials is null)
			{
				return;
			}

			ApiKey = TranslationOptions.EdgeCredentials.ApiKey;
			Host = TranslationOptions.EdgeCredentials.Uri.ToString();
			Password = TranslationOptions.EdgeCredentials.Password;
			Username = TranslationOptions.EdgeCredentials.UserName;
		}

		private async void SignIn(object parameter)
		{
			if (!HostIsValid()
			 || !CredentialsAreSet()
			 || !IsAuthenticationTypeSelected)
			{
				StopLoginProcess?.Invoke(this, EventArgs.Empty);
				ErrorHandling.ShowDialog(null, PluginResources.Connection_Credentials, PluginResources.Connection_Error_NoCredentials);
				return;
			}

			StartLoginProcess?.Invoke(this, new LoginEventArgs(PluginResources.Loading_Connecting));
			var edgeCredentials = new EdgeCredentials(Host)
			{
				UserName = Username,
				Password = Password,
				ApiKey = ApiKey
			};

			(bool Success, Exception Error) response;
			if (IsApiKeySelected)
			{
				response = await EdgeService.VerifyAPI(edgeCredentials, TranslationOptions);
			}
			else if (IsCredentialsSelected)
			{
				response = await EdgeService.AuthenticateUser(edgeCredentials, TranslationOptions);
			}
			else
			{
				var vm = new EdgeAuth0ViewModel(edgeCredentials, TranslationOptions);
				var view = new EdgeAuth0View() { DataContext = vm };
				view.ShowDialog();
				response = (vm.Success, vm.Exception);
			}

			StopLoginProcess?.Invoke(this, EventArgs.Empty);
			if (!response.Success)
			{
				ErrorHandling.ShowDialog(response.Error, "Authentication failed", "The authentication failed", true);
				return;
			}

			TranslationOptions.EdgeCredentials = edgeCredentials;
			TranslationOptions.AuthenticationType = AuthenticationType;
			TranslationOptions.PluginVersion = PluginVersion.LanguageWeaverEdge;
			TranslationOptions.UpdateUri();
			CloseWindow();
		}

		private void Back(object parameter)
		{
			AuthenticationType = AuthenticationType.None;
		}

		private void Clear(object parameter)
		{
			if (parameter is not string parameterString)
			{
				return;
			}

			switch (parameterString)
			{
				case nameof(Username):
					Username = string.Empty;
					break;

				case nameof(Host):
					Host = string.Empty;
					break;

				default:
					break;
			}
		}

		private void SelectAuthenticationType(object parameter)
		{
			if (parameter is not AuthenticationType authenticationType)
			{
				return;
			}

			AuthenticationType = authenticationType;
		}

		private bool HostIsValid()
		{
			if (string.IsNullOrEmpty(Host))
			{
				return false;
			}

			var host = Host.Trim();
			if (string.IsNullOrEmpty(host))
			{
				return false;
			}

			if (!Host.StartsWith("http://") && !Host.Contains("https://"))
			{
				return false;
			}

			try
			{
				var uri = new Uri(Host);
				return true;
			}
			catch
			{
				return false;
			}
		}

		private bool CredentialsAreSet()
		{
			if (AuthenticationType == AuthenticationType.EdgeSSO)
			{
				return true;
			}

			return IsCredentialsSelected
				? !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password)
				: !string.IsNullOrEmpty(ApiKey);
		}
	}
}