using System.Collections.Generic;
using System.Text.RegularExpressions;
using VsChromium.Core.Configuration;

namespace VsChromium.Server.Projects
{
    class PropertyCollection : IPropertyCollection {
        private readonly Dictionary<string, string> _properties = new Dictionary<string, string>();

        public PropertyCollection(IConfigurationSectionContents contents) {
            foreach(string line in contents.Contents) {
                Match match = Regex.Match(line, @"(?<name>\w*)\s*=\s*(?<value>\w*)");
                if(match.Success) {
                    _properties.Add(match.Groups["name"].Value.ToString(), match.Groups["value"].Value.ToString());
                }
            }
        }

        public bool TryGetBool(string propertyName, out bool value) {
            string strValue;

            if (_properties.TryGetValue(propertyName, out strValue)) {
                return bool.TryParse(strValue, out value);
            }

            value = default(bool);
            return false;
        }

        public bool TryGetInt(string propertyName, out int value)
        {
            string strValue;

            if (_properties.TryGetValue(propertyName, out strValue))
            {
                return int.TryParse(strValue, out value);
            }

            value = default(int);
            return false;
        }

        public bool TryGetString(string propertyName, out string value)
        {
            if (_properties.TryGetValue(propertyName, out value))
            {
                return true;
            }

            value = default(string);
            return false;
        }
    }
}
