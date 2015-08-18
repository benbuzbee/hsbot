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
        public enum CardFamily { MURLOC = 14, DEMON = 15, MECH = 17, BEAST = 20, TOTEM = 21, PIRATE = 23, DRAGON = 24, UNKNOWN = 0xFF }

        /// <summary>
        /// The name for a card
        /// </summary>
        public String Name
        {
            get;
            set;
        }

        /// <summary>
        /// The text in hand description of a card
        /// </summary>
        public String Description
        {
            get;
            set;
        }

        public String FlavorText
        {
            get;
            set;
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
        public CardFamily Family { get; set; }

        public String XmlData { get; set; }


        public Card(String entityCardID) { ID = entityCardID; Family = CardFamily.UNKNOWN;  }

        public int Type { get; set; }




        /**
         * Returns the description for the given localization, null if there is not one for this localization, or an empty string if there is no description for any localization
         * */

        private static Regex modifyableNumber = new Regex(@"\$(?<value>\d+)");
       
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

        /// <summary>
        /// Gets an mIRC printable and marked up name
        /// </summary>
        /// <returns></returns>
        public String GetmIRCName(bool fControlCodes = true)
        {
            if (fControlCodes)
            {
                return String.Format("[{0}{1}]", GetmIRCColor(), HTML2mIRC(Name));
            }
            else
            {
                return String.Format("[{0}]", NoHTML(Name));
            }
        }


        public String GetFullText(bool fControlCodes = true)
        {
            StringBuilder sb = new StringBuilder(2048);
            sb.AppendFormat("{0}: ", GetmIRCName(fControlCodes));

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
            
            // Print the release of the card from the card ID
            // Format some known ones a little more nicely
            if (ID.Contains("_"))
            {
                String release = ID.Substring(0, ID.IndexOf('_'));
 
                if (release.StartsWith("NAX"))
                {
                    sb.Append("- Naxxramas ");
                }
                else if (release.StartsWith("BRMA"))
                {
                    sb.Append("- BRM (AI) ");
                }
                else 
                {
                    sb.AppendFormat("- {0} ",release);
                }
                
            }

            if (Family != CardFamily.UNKNOWN)
            {
                String strFamily = null;
                switch (Family)
                {
                    case CardFamily.BEAST:
                        strFamily = "Beast";
                        break;
                    case CardFamily.MECH:
                        strFamily = "Mech";
                        break;
                    case CardFamily.DEMON:
                        strFamily = "Demon";
                        break;
                    case CardFamily.DRAGON:
                        strFamily = "Dragon";
                        break;
                    case CardFamily.MURLOC:
                        strFamily = "Murloc";
                        break;
                    case CardFamily.PIRATE:
                        strFamily = "Pirate";
                        break;
                    case CardFamily.TOTEM:
                        strFamily = "Totem";
                        break;
                    default:
                        System.Diagnostics.Debug.Assert(false, "Unknown family");
                        break;
                }
                if (strFamily != null)
                {
                    if (fControlCodes)
                    {
                        sb.AppendFormat("- {0} ", strFamily);
                    }
                    else
                    {
                        sb.Append("- ");
                        sb.Append(strFamily);
                        sb.Append(" ");
                    }
                }
            }

            if (!String.IsNullOrEmpty(Description))
            {
                String strNewDescription = ReplaceDollarWithStar(Description.Replace('\n', ' '));
                sb.AppendFormat("- {0} ", fControlCodes ? HTML2mIRC(strNewDescription) : NoHTML(strNewDescription));
                
            }

            if (!String.IsNullOrEmpty(FlavorText))
            {
                sb.AppendFormat("- \"{0}\"",  fControlCodes ? HTML2mIRC(FlavorText) : NoHTML(FlavorText));
            }

            return sb.ToString();
        }
        /// <summary>
        /// Replaces $number with *number* for all occurences in a string. Does not modify the original
        /// </summary>
        /// <param name="original"></param>
        /// <returns></returns>
        private String ReplaceDollarWithStar(String original)
        {
            // Most descriptions don't have a dollar sign. Don't waste time.
            if (!original.Contains("$"))
            {
                return original;
            }

            // Create a SB to store results. Same as the original with room for 1 more *. If more are needed (rare case) it will be resized.
            StringBuilder sb = new StringBuilder(original.Length + 1);

            // Position in original string relative to what we have copied to the string builder.
            int iPosition;
            // Position of dollar sign
            int iDollar;

            // Loop all dollar signs. Copy everything before the dollar over, then *number*.
            for (iPosition = 0; (iDollar = original.IndexOf('$', iPosition)) >= 0; /* no-op */)
            {
                // Append everything from we haven't already appended up to this dollar sign
                sb.Append(original.Substring(iPosition, iDollar - iPosition));

                sb.Append('*');
                iPosition = iDollar + 1; // Skip the $
                // Move through the number
                while (iPosition < original.Length && char.IsDigit(original[iPosition]))
                {
                    sb.Append(original[iPosition]);
                    ++iPosition;
                }

                // Add a star after the number
                sb.Append('*');
            }
            
            // Append anything remaining in the original string to the builder
            if (iPosition < original.Length)
                sb.Append(original.Substring(iPosition));

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
        /// <summary>
        /// Removes common HTML elements
        /// </summary>
        /// <param name="s">String with HTML elements</param>
        /// <returns>String with common HTML elements removed or replaced</returns>
        private String NoHTML(String s)
        {
            return s.Replace("<b>", "").Replace("</b>", "").Replace("<i>", "").Replace("</i>", "").Replace("\\n", ". ");
        }
    }
}
