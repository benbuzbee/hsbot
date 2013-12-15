using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace HSBot.Cards
{
    class Card
    {

        public enum RarityValues { UNKNOWN, COMMON, FREE, RARE, EPIC, LEGENDARY };
        public enum ClassValues { ALL, DRUID = 2, HUNTER = 3, MAGE = 4, PALADIN = 5, PRIEST = 6, ROGUE = 7, SHAMAN = 8, WARLOCK = 9, WARRIOR = 10 };
        public enum CardType { WEAPON = 7, HERO = 3 };

        private Dictionary<String, String> localizedNames = new Dictionary<String, String>();
        private Dictionary<String, String> localizedDescriptions = new Dictionary<String, String>();

        /**
         * The name of this card, for enUS
         * */
        public String Name
        {
            get
            {
                return GetName();
            }
        }

        public String Description
        {
            get
            {
                return GetDescription();
            }
        }

        public String ID
        {
            get;
            private set;
        }

        public int Attack { get; set; }
        public int Health { get; set; }
        public int Cost { get; set; }
        public ClassValues Class { get; set; }
        public RarityValues Rarity { get; set; }

        public String XmlData { get; set; }

        public String XmlSource { get; set; }

        public Card(String entityCardID) { ID = entityCardID; }

        public int Type { get; set; }

        public void SetName(String localization, String name)
        {
            localizedNames.Add(localization, name);
        }

        public String GetName(String localization = "enUS")
        {
            String name = null;
            if (localizedNames.TryGetValue(localization, out name))
                return name;
            else
                return null;
        }

        public void SetDescription(String localization, String Description)
        {
            localizedDescriptions.Add(localization, Description);
        }

        /**
         * Returns the description for the given localization, null if there is not one for this localization, or an empty string if there is no description for any localization
         * */

        private Regex modifyableNumber = new Regex(@"\$(?<value>\d+)");
        public String GetDescription(String localization = "enUS")
        {
            // Some cards have no description
            if (localizedDescriptions.Values.Count == 0)
            {
                return "";
            }

            String desc = null;
            if (localizedDescriptions.TryGetValue(localization, out desc))
            {
                desc = modifyableNumber.Replace(desc, "*${value}*");
                return desc;
            }
            else
                return null;
        }


        private String GetColor()
        {
            switch (Rarity)
            {
                case RarityValues.FREE:
                    return "05";
                case RarityValues.COMMON:
                    return "03";
                case RarityValues.RARE:
                    return "12";
                case RarityValues.EPIC:
                    return "06";
                case RarityValues.LEGENDARY:
                    return "07";
                default:
                    return "";
            }
        }

        public String GetFullText()
        {
            StringBuilder sb = new StringBuilder(2048);

            sb.AppendFormat("[{0}{1}]: ", GetColor(), Name);
            if (Attack != 0 || Health != 0)
                sb.AppendFormat("{0}/{1}: ", Attack, Health);
            sb.AppendFormat("Cost: {0} ", Cost);

            switch (Class)
            {
                case ClassValues.ALL:
                    sb.Append("- Usable by all classes ");
                    break;
                case ClassValues.MAGE:
                    sb.Append("- Mages only ");
                    break;
                case ClassValues.ROGUE:
                    sb.Append("- Rogues only ");
                    break;
                case ClassValues.DRUID:
                    sb.Append("- Druids only ");
                    break;
                case ClassValues.HUNTER:
                    sb.Append("- Hunters only ");
                    break;
                case ClassValues.PALADIN:
                    sb.Append("- Paladins only ");
                    break;
                case ClassValues.WARLOCK:
                    sb.Append("- Warlocks only ");
                    break;
                case ClassValues.WARRIOR:
                    sb.Append("- Warriors only ");
                    break;
                case ClassValues.PRIEST:
                    sb.Append("- Priests only ");
                    break;
                case ClassValues.SHAMAN:
                    sb.Append("- Shamans only ");
                    break;
            }

            if (Type == (int)CardType.WEAPON) { sb.Append("- Weapon "); }

            if (!String.IsNullOrEmpty(Description))
            {
                sb.AppendFormat("- {0} ", Description);
                
            }

            return sb.ToString();
        }
    }
}
