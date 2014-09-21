using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace HSBot.Cards
{
    /// <summary>
    /// Represents a HearthStone card
    /// </summary>
    public class Card
    {

        public enum RarityValues { UNKNOWN, COMMON, FREE, RARE, EPIC, LEGENDARY };
        public enum ClassValues { ALL, DRUID = 2, HUNTER = 3, MAGE = 4, PALADIN = 5, PRIEST = 6, ROGUE = 7, SHAMAN = 8, WARLOCK = 9, WARRIOR = 10 };
        public enum CardType { WEAPON = 7, HERO = 3, CREATURE = 4, EFFECT = 6 /* Cabal Control */ };

        private Dictionary<String, String> localizedNames = new Dictionary<String, String>();
        private Dictionary<String, String> localizedDescriptions = new Dictionary<String, String>();
        private Dictionary<String, String> localizedFlavorText = new Dictionary<String, String>();

        /// <summary>
        /// The en-US localized name for a card
        /// </summary>
        public String Name
        {
            get
            {
                return GetName();
            }
        }

        /// <summary>
        /// The en-US TextInHand for a card
        /// </summary>
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


        public Card(String entityCardID) { ID = entityCardID; }

        public int Type { get; set; }

        public String FlavorText { 
            get {
                return GetFlavorText();
            }
        }

        public void SetName(String localization, String name)
        {
            localizedNames.Add(localization, name);
        }

        /// <summary>
        /// Gets the name of a card for the specified localization string
        /// </summary>
        /// <param name="localization"></param>
        /// <returns></returns>
        public String GetName(String localization = "enUS")
        {
            String name = null;
            if (localizedNames.TryGetValue(localization, out name))
                return name;
            else
                return null;
        }

        /// <summary>
        /// Sets the text in hand for a card and given localization string
        /// </summary>
        /// <param name="localization"></param>
        /// <param name="Description"></param>
        public void SetDescription(String localization, String Description)
        {
            localizedDescriptions.Add(localization, Description);
        }

        /// <summary>
        /// Sets the flavor text for a card and given localization string
        /// </summary>
        /// <param name="localization"></param>
        /// <param name="Description"></param>
        public void SetFlavorText(String localization, String Description)
        {
            localizedFlavorText.Add(localization, Description);
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

        public String GetFlavorText(String localization = "enUS")
        {
            // Some cards have no description
            if (localizedFlavorText.Values.Count == 0)
            {
                return "";
            }

            String flavor = null;
            if (localizedFlavorText.TryGetValue(localization, out flavor))
            {
                flavor = modifyableNumber.Replace(flavor, "*${value}*");
                return flavor;
            }
            else
                return null;
        }


        private String GetmIRCColor()
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

            sb.AppendFormat("[{0}{1}]: ", GetmIRCColor(), HTML2mIRC(Name));
            if (Attack != 0 || Health != 0)
                sb.AppendFormat("{0}/{1}: ", Attack, Health);
            sb.AppendFormat("Cost: {0} ", Cost);

            switch (Class)
            {
                case ClassValues.ALL:
                    sb.Append("- All classes ");
                    break;
                case ClassValues.MAGE:
                    sb.Append("- Mages ");
                    break;
                case ClassValues.ROGUE:
                    sb.Append("- Rogues ");
                    break;
                case ClassValues.DRUID:
                    sb.Append("- Druids ");
                    break;
                case ClassValues.HUNTER:
                    sb.Append("- Hunters ");
                    break;
                case ClassValues.PALADIN:
                    sb.Append("- Paladins ");
                    break;
                case ClassValues.WARLOCK:
                    sb.Append("- Warlocks ");
                    break;
                case ClassValues.WARRIOR:
                    sb.Append("- Warriors ");
                    break;
                case ClassValues.PRIEST:
                    sb.Append("- Priests ");
                    break;
                case ClassValues.SHAMAN:
                    sb.Append("- Shamans ");
                    break;
            }

            if (Type == (int)CardType.WEAPON) { sb.Append("- Weapon "); }

            if (!String.IsNullOrEmpty(Description))
            {
                sb.AppendFormat("- {0} ", HTML2mIRC(Description));
                
            }

            if (!String.IsNullOrEmpty(FlavorText))
            {
                sb.AppendFormat("- \"{0}\"", HTML2mIRC(FlavorText));
            }

            return sb.ToString();
        }
        /// <summary>
        /// Replaces HTML elements with mIRC equivilients
        /// </summary>
        /// <param name="s">String with HTML elements</param>
        /// <returns>String with mappable HTML elements replaced with mIRC control codes</returns>
        private String HTML2mIRC(String s)
        {
            return s.Replace("<b>", "").Replace("</b>", "").Replace("<i>", "").Replace("</i>", "").Replace("\\n", ". ");
        }
    }
}
