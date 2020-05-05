﻿using System;
using System.Collections.Generic;
using Sdl.Community.IATETerminologyProvider.Helpers;
using Sdl.Community.IATETerminologyProvider.Model;
using Sdl.Community.IATETerminologyProvider.Service;
using Sdl.Terminology.TerminologyProvider.Core;

namespace Sdl.Community.IATETerminologyProvider
{
	[TerminologyProviderFactory(Id = "IATETerminologyProvider",	Name = "IATE Terminology Provider", Icon= "Iate_logo", Description = "IATE terminology provider factory")]
	public class IATETerminologyProviderFactory : ITerminologyProviderFactory
	{				
		public bool SupportsTerminologyProviderUri(Uri terminologyProviderUri)
		{
			return terminologyProviderUri.Scheme == Constants.IATEGlossary;
		}

		public ITerminologyProvider CreateTerminologyProvider(Uri terminologyProviderUri, ITerminologyProviderCredentialStore credentials)
		{
			var settingsService  = new IateSettingsService();
			var savedSettings = settingsService.GetProviderSettings();
			var providerSettings = new SettingsModel
			{
				Domains = new List<DomainModel>(),
				TermTypes = new List<TermTypeModel>()
			};
			if (savedSettings != null)
			{
				providerSettings.Domains.AddRange(savedSettings.Domains);
				providerSettings.TermTypes.AddRange(savedSettings.TermTypes);
			}
			var terminologyProvider = new IATETerminologyProvider(providerSettings);
			return terminologyProvider;
		}		
	}
}