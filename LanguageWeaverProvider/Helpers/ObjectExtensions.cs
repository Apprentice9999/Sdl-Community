﻿using System.Collections.Generic;

namespace LanguageWeaverProvider.Extensions
{
	public static class ObjectExtensions
	{
		public static Dictionary<string, string> ToKeyValuePairDictionary(this object obj)
		{
			var type = obj.GetType();
			var properties = type.GetProperties();
			var keyValuePairs = new Dictionary<string, string>();
			foreach (var property in properties)
			{
				var key = property.Name;
				var value = property.GetValue(obj);
				if (value is null || value is not string stringValue)
				{
					continue;
				}

				key = char.ToLower(key[0]) + key.Substring(1);
				keyValuePairs.Add(key, stringValue);
			}

			return keyValuePairs;
		}
	}
}
