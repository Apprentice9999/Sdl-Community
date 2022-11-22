﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using Sdl.Core.Globalization;

namespace TMX_Lib.TmxFormat
{
	
	public class TmxParser : IDisposable
	{

		private string _fileName;
		// if non-empty -> error parsing the file
		private string _error = "";

		public bool HasError => _error != "";
		public string Error => _error;

		private string _sourceLanguage, _targetLanguage;

		// there's a 1-to-1 correspondence between the TMX file' /tu nodes and the TmxTranslationUnit objects
		private List<TmxTranslationUnit> _translations = new List<TmxTranslationUnit>();

        public IReadOnlyList<TmxTranslationUnit> TranslationUnits => _translations;


        private TmxHeader _header;
        public TmxHeader Header {
	        get
	        {
				lock(this)
					return _header;
			}
        }

        public TmxParser(string fileName)
		{
			_fileName = fileName;
			// note: loading async is a really bad idea, since all calls to the language direction are sync
			// processing the nodes is async, but at the end of this function, we'll know the source + target languages
			Load();
		}

        private void ParseHeader(XmlDocument document)
        {
            string sourceLanguage = document.SelectSingleNode("tmx/header/@srclang")?.InnerText ?? "";
            string targetLanguage = document.SelectSingleNode("tmx/body/tu[1]/tuv[2]")?.Attributes?[0].InnerText ?? "";
            List<string> domains = new List<string>();

            var domainsStr = document.SelectSingleNode("tmx/header/prop[@type='x-Domain:SinglePicklist']")?.InnerText ?? "";
            if (domainsStr != "")
                domains = domainsStr.Split(',').Select(s => s.Trim()).ToList();
            var xml = document.SelectSingleNode("tmx/header")?.OuterXml ?? "";
            var creationDateStr = GetAttribute(document.SelectSingleNode("tmx/header"), "creationdate");
            var author = GetAttribute(document.SelectSingleNode("tmx/header"), "creationid");
            DateTime? creationDate = null;
            if (creationDateStr != "")
                creationDate = Iso8601Date(creationDateStr);

			lock(this)
				_header = new TmxHeader(sourceLanguage, targetLanguage, domains, creationDate, author, xml);
        }

        private Task _loadTask;
        private void Load()
		{
			_error = "";
			try
			{
				List<TmxTranslationUnit> translations = new List<TmxTranslationUnit>();
				XmlReaderSettings settings = new XmlReaderSettings();
				settings.XmlResolver = null;
				settings.DtdProcessing = DtdProcessing.Ignore;
				XmlReader xmlReader = XmlTextReader.Create(_fileName, settings);

				var document = new XmlDocument();
				document.Load(xmlReader);
                ParseHeader(document);

                _loadTask = Task.Run(() =>
                {
	                foreach (XmlNode item in document.SelectNodes("//tu"))
		                translations.Add(NodeToTU(item));

	                lock (this)
		                _translations = translations;
                });
			}
			catch (Exception e)
			{
				_error = $"There has been an error parsing {_fileName}: {e.Message}";
			}
		}

        public async Task LoadAsync()
        {
	        if (_loadTask != null)
		        await _loadTask;
        }

		// if not found, returns ""
        private static string GetAttribute(XmlNode node, string attributeName)
        {
            var found = node.Attributes?.OfType<XmlAttribute>().FirstOrDefault(a => a.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
            var value = found?.Value;
            return value ?? "";
        }

        private static readonly string[] iso8061formats = { 
            "yyyyMMddTHHmmsszzz",
            "yyyyMMddTHHmmsszz",
            "yyyyMMddTHHmmssZ",
            "yyyy-MM-ddTHH:mm:sszzz",
            "yyyy-MM-ddTHH:mm:sszz",
            "yyyy-MM-ddTHH:mm:ssZ",
            "yyyyMMddTHHmmzzz",
            "yyyyMMddTHHmmzz",
            "yyyyMMddTHHmmZ",
            "yyyy-MM-ddTHH:mmzzz",
            "yyyy-MM-ddTHH:mmzz",
            "yyyy-MM-ddTHH:mmZ",
            "yyyyMMddTHHzzz",
            "yyyyMMddTHHzz",
            "yyyyMMddTHHZ",
            "yyyy-MM-ddTHHzzz",
            "yyyy-MM-ddTHHzz",
            "yyyy-MM-ddTHHZ"
        };
        private static DateTime Iso8601Date(string date)
        {
            return DateTime.ParseExact(date, iso8061formats, CultureInfo.InvariantCulture, DateTimeStyles.None);
        }

		private TmxTranslationUnit NodeToTU(XmlNode xmlUnit)
		{
			var source = xmlUnit.SelectSingleNode("tuv[1]/seg");
			var target = xmlUnit.SelectSingleNode("tuv[2]/seg");
            var tu = new TmxTranslationUnit
            {
				SourceLanguage = _header.SourceLanguage, TargetLanguage = _header.TargetLanguage,
            };
            tu.Source = NoteToTextPart(source);
            if (target != null)
                tu.Target = NoteToTextPart(target);

            var creationDate = GetAttribute(xmlUnit, "creationdate");
            var creationAuthor = GetAttribute(xmlUnit, "creationid");
            var changeDate = GetAttribute(xmlUnit, "changedate");
            var changeAuthor = GetAttribute(xmlUnit, "changeid");

			if (creationDate != "")
                tu.CreationTime = Iso8601Date(creationDate);
            tu.CreationAuthor = creationAuthor;
            if (changeDate != "")
                tu.ChangeTime = Iso8601Date(changeDate);
            tu.ChangeAuthor = changeAuthor;

			// confirmation level
			var confirmationLevel = xmlUnit.SelectSingleNode("prop[@type='x-ConfirmationLevel']")?.InnerText ?? "";
			if (confirmationLevel != "" && Enum.TryParse<ConfirmationLevel>(confirmationLevel, out var cf))
				tu.ConfirmationLevel = cf;
            // domain
            var domain = xmlUnit.SelectSingleNode("prop[@type='x-Domain:SinglePicklist']");
            tu.Domain = domain?.InnerText ?? "";
            return tu;
        }

		private List<TmxFormattedTextPart> NoteToTextPart(XmlNode segNode)
        {
            List<TmxFormattedTextPart> list = new List<TmxFormattedTextPart>();
            foreach (XmlNode item in segNode.ChildNodes)
            {
                if (item.NodeType == XmlNodeType.Text)
                {
					list.Add(new TmxFormattedTextPart { Text = item.InnerText });
                } else if (item.NodeType == XmlNodeType.Element)
                {
                    var tp = new TmxFormattedTextPart { FormatType = item.Name };
					if ( item.Attributes != null)
                        foreach (XmlAttribute attribute in item.Attributes)
                            tp.FormatAttributes.Add( (attribute.Name, attribute.Value));
                    list.Add(tp);
                }
            }

            return list;
        }

		public void Dispose()
		{
		}
	}
}
