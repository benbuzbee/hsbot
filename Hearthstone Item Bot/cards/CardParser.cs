using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Xml;

namespace HSBot.Cards
{
    static class CardParser
    {
        public enum CardTag
        {
            HEALTH = 45,
            ATTACK = 47,
            COST = 48,
            DESCRIPTION = 184,
            NAME = 185,
            DURABILITY = 187,
            CLASS_ID = 199,
            RARITY = 203,
            FLAVOR_TEXT = 351
            
        }
        /// <summary>
        /// Reads file with card data XML entries 
        /// </summary>
        /// <param name="fromFile">card data xml file</param>
        /// <returns>A map of loc string -> XmlDocument - the XmlDocument represents a CardDefs element whose children, Entity, represents a card.  Call GetCards to parse those.</returns>
        public static Dictionary<String, XmlDocument> Extract(String fromFile)
        {
            Dictionary<String, XmlDocument> docs = new Dictionary<String, XmlDocument>();


            Console.WriteLine("Extracting card data...");

            // Queue of loc string -> position pair
            // position pair represents the start and end of the <CardDefs> entity
            Queue<KeyValuePair<String, KeyValuePair<long, long>>> xmlOffsets = new Queue<KeyValuePair<String, KeyValuePair<long, long>>>();

            // This pass through the file locates the CardPairs positions for every loc
            using (FileStream input = File.OpenRead(fromFile))
            { 
                while (input.Position < input.Length)
                {
                    // Find loc string
                    input.SeekForAscii("\0\0\0\0\x04\0\0\0");
                    // Read loc string
                    byte[] locBuf = new byte[4];
                    input.Read(locBuf, 0, locBuf.Length);
                    String loc = Encoding.ASCII.GetString(locBuf, 0, locBuf.Length);

                    // Seek past 4 unknown bytes to start of <CardDefs>
                    input.Seek(4, SeekOrigin.Current);
                    long start = input.Position;
                    
                    // Find end
                    input.SeekForAscii("</CardDefs>");
                    long end = input.Position;

                    var positionPair = new KeyValuePair<long, long>(start, end);
                    xmlOffsets.Enqueue(new KeyValuePair<String, KeyValuePair<long, long>>(loc, positionPair));

                }
                input.Close();
            }

            int count = 0;
            // This pass through the file reads the CardDefs entity as an XmlDoc
            using (FileStream input = File.OpenRead(fromFile))
            {
                for (count = 0; xmlOffsets.Count > 0; ++count)
                {
                    var pairpair = xmlOffsets.Dequeue();
                    String loc = pairpair.Key;
                    long start = pairpair.Value.Key;
                    long end = pairpair.Value.Value;
                    input.Seek(start, SeekOrigin.Begin);
                    byte[] data = new byte[end - start];
                    input.Read(data, 0, (int)(end - start));

                    XmlDocument document = new XmlDocument();
                    try
                    {
                        String sXml = Encoding.UTF8.GetString(data);
                        document.LoadXml(sXml);
                        docs.Add(loc, document);
                    }
                    catch (Exception)
                    {
                        Console.WriteLine("Exception parsing an XML document");
                    }
                }

                input.Close();
            }

            Console.WriteLine("Done extracting cards for {0} localizations.", count);
            return docs;
        }

        /// <summary>
        /// Seeks the stream for this ascii set. Kind of.  In reality it will not respect repeats correctly because if it determines
        /// that a byte does not match what it should, it will not reset its running match count if it matches the previous bytes
        /// yada yada yada lets just say it works for my purposes in this bot but I would not expect it to work for yours!
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="text"></param>
        public static void SeekForAscii(this Stream stream, String text)
        {
            
            int textPtr = 0;
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            int b;
            while (true)
            {
                b = stream.ReadByte();
                if (b < 0) break;
                if (b == bytes[textPtr])
                {
                    ++textPtr;
                    if (textPtr == bytes.Length)
                        return;
                }
                else
                {
                    // If we don't match this one, but we do match the previous one, don't reset the pointer.  
                    if (textPtr > 0 && b == bytes[textPtr - 1])
                        continue;
                    else
                        textPtr = 0;
                }

            }
        }
        private static String GetTagValue(XmlNode nodeEntity, CardTag tagId)
        {
            XmlNode nodeTag = nodeEntity.SelectSingleNode("Tag[@enumID=\""+(int)tagId+"\"]");
            if (nodeTag == null)
                return null;
            var attribute = nodeTag.Attributes["value"];
            return attribute != null ? attribute.Value : null;
        }
        private static bool TryGetTagIntValue(XmlNode entity, CardTag tagId, out int id)
        {
            String strValue = GetTagValue(entity, tagId);

            if (strValue == null)
            {
                id = 0;
                return false;
            }
            return int.TryParse(strValue, out id);

        }
        private static String GetTagInnerText(XmlNode nodeEntity, CardTag targId)
        {
            XmlNode nodeTag = nodeEntity.SelectSingleNode("Tag[@enumID=\"" + (int)targId + "\"]");
            if (nodeTag == null)
                return null;
            return nodeTag.InnerText;
        }
        // Parses Entity tags from the XmlDoc
        public static List<Card> GetCards(XmlDocument doc)
        {
            List<Card> cards = new List<Card>();
            var entityNodes = doc.SelectNodes("//Entity");
            foreach (XmlNode entity in entityNodes)
            {
                try
                {
                    XmlAttribute entityCardID = entity.Attributes["CardID"];

                    if (entityCardID == null)
                    {
                        Console.Error.WriteLine("Card had no CardID");
                        continue;
                    }

                    String cardName = GetTagInnerText(entity, CardTag.NAME);
                    if (cardName == null)
                    {
                        Console.Error.WriteLine("Card had no card name tag");
                        continue;
                    }

                    Card card = new Card(entityCardID.Value);
                    card.XmlData = entity.ToString();

                    card.Name = cardName;

                    card.Description = GetTagInnerText(entity, CardTag.DESCRIPTION);

                    int iValue;

                    if (TryGetTagIntValue(entity, CardTag.ATTACK, out iValue))
                    {
                        card.Attack = iValue;
                    }

                    if (TryGetTagIntValue(entity, CardTag.HEALTH, out iValue))
                    {
                        card.Health = iValue;
                    }

                    if (TryGetTagIntValue(entity, CardTag.COST, out iValue))
                    {
                        card.Cost = iValue;
                    }

                    int iDurability;
                    if (TryGetTagIntValue(entity, CardTag.DURABILITY, out iDurability))
                    {
                        card.Health = iDurability;
                    }

                    int iClassId;
                    if (TryGetTagIntValue(entity, CardTag.CLASS_ID, out iClassId))
                    {
                        switch (iClassId)
                        {
                            case (int)Card.ClassValues.MAGE:
                                card.Class = Card.ClassValues.MAGE;
                                break;
                            case (int)Card.ClassValues.SHAMAN:
                                card.Class = Card.ClassValues.SHAMAN;
                                break;
                            case (int)Card.ClassValues.ROGUE:
                                card.Class = Card.ClassValues.ROGUE;
                                break;
                            case (int)Card.ClassValues.PALADIN:
                                card.Class = Card.ClassValues.PALADIN;
                                break;
                            case (int)Card.ClassValues.PRIEST:
                                card.Class = Card.ClassValues.PRIEST;
                                break;
                            case (int)Card.ClassValues.WARRIOR:
                                card.Class = Card.ClassValues.WARRIOR;
                                break;
                            case (int)Card.ClassValues.WARLOCK:
                                card.Class = Card.ClassValues.WARLOCK;
                                break;
                            case (int)Card.ClassValues.DRUID:
                                card.Class = Card.ClassValues.DRUID;
                                break;
                            case (int)Card.ClassValues.HUNTER:
                                card.Class = Card.ClassValues.HUNTER;
                                break;
                            default:
                                card.Class = Card.ClassValues.ALL;
                                Console.Error.WriteLine("Unknown class: {0} Class {1}", card.Name, iClassId);
                                break;
                        }
                    }
                    else
                        card.Class = Card.ClassValues.ALL;

                    string rarity = GetTagValue(entity, CardTag.RARITY);
                    int iRarity;

                    if (TryGetTagIntValue(entity, CardTag.RARITY, out iRarity))
                    {
                        switch (iRarity)
                        {
                            case 1:
                                card.Rarity = Card.RarityValues.COMMON;
                                break;
                            case 2:
                                card.Rarity = Card.RarityValues.FREE;
                                break;
                            case 3:
                                card.Rarity = Card.RarityValues.RARE;
                                break;
                            case 4:
                                card.Rarity = Card.RarityValues.EPIC;
                                break;
                            case 5:
                                card.Rarity = Card.RarityValues.LEGENDARY;
                                break;
                            default:
                                card.Rarity = Card.RarityValues.UNKNOWN;
                                break;
                        }
                    }
                    else
                        card.Rarity = Card.RarityValues.UNKNOWN;

                    /*
                    XmlNode type = entity.SelectSingleNode("Tag[@name=\"CardType\"]");
                    if (type != null)
                    {
                        card.Type = int.Parse(type.Attributes["value"].Value);
                        if (card.Type == (int)Card.CardType.HERO || card.Type == (int)Card.CardType.EFFECT) // Heros? 3 included the Hero "Hogger" 0/10 -- 4 may be creatures -- 7 may be weapons (warrior)
                            continue;
                    }
                    */

                    card.FlavorText = GetTagInnerText(entity, CardTag.FLAVOR_TEXT);
                    
                    cards.Add(card);
                }
                catch (XmlException exception)
                {
                    Console.Error.WriteLine("There was an XML error while parsing a card.");
                    Console.Error.WriteLine(exception);
                }


            }
            Card hearthbot = new Card("CUSTOM_HEARTHBOT");
            hearthbot.Name = "HearthBot";
            hearthbot.Cost = 1;
            hearthbot.Health = hearthbot.Attack = 50;
            hearthbot.Class = Card.ClassValues.ALL;
            hearthbot.Description = @"<b>Battlecry:</b> Destroy all secrets and deal 100 damage to the enemy hero. To get it, go here: https://github.com/aca20031/hsbot/";
            cards.Add(hearthbot);
            return cards;
        }
    }
}
