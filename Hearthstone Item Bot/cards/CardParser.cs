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
        /// <summary>
        /// Reads file with card data XML entrities and returns each unique document
        /// </summary>
        /// <param name="fromFile"></param>
        /// <returns></returns>
        public static XmlDocument[] Extract(String fromFile)
        {
            List<XmlDocument> _docs = new List<XmlDocument>();


            Console.WriteLine("Extracting card data...");

            Queue<long> xmlOffsets = new Queue<long>();
            using (FileStream input = File.OpenRead(fromFile))
            { 
                while (input.Position < input.Length)
                {
   
                    input.SeekForAscii("<?xml");
                    xmlOffsets.Enqueue(input.Position - "<?xml".Length);
                    input.SeekForAscii("\0");
                    xmlOffsets.Enqueue(input.Position - 1);

                }
                input.Close();
            }
            using (FileStream input = File.OpenRead(fromFile))
            {
                for (int count = 0; xmlOffsets.Count > 0; ++count)
                {
                    long start = xmlOffsets.Dequeue();
                    long end = xmlOffsets.Dequeue();
                    input.Seek(start, SeekOrigin.Begin);
                    byte[] data = new byte[end - start];
                    input.Read(data, 0, (int)(end - start));

                    XmlDocument document = new XmlDocument();
                    try
                    {
                        document.LoadXml(Encoding.UTF8.GetString(data));
                        _docs.Add(document);
                    }
                    catch (Exception)
                    {
                        Console.WriteLine("Exception parsing an XML document");
                    }
                }
            }

            Console.WriteLine("Done extracting cards.");
            return _docs.ToArray();
        }

        public static void SeekForAscii(this Stream stream, String text)
        {
            
            int textPtr = 0;
            int b;
            while (true)
            {
                b = stream.ReadByte();
                if (b < 0) break;
                if (b == text[textPtr])
                {
                    ++textPtr;
                    if (textPtr == text.Length)
                        return;
                }
                else
                    textPtr = 0;

            }
        }

        public static List<Card> GetCards(XmlDocument[] xmlDocs)
        {
            List<Card> cards = new List<Card>();
            foreach (XmlDocument document in xmlDocs)
            {
                try
                {
                    //<Entity version="2" CardID="XXX_039">
                    XmlNode entity = document.DocumentElement.SelectSingleNode("//Entity");

                    if (entity == null)
                    {
                        Console.Error.WriteLine("Card had no CardID");
                        continue;
                    }

                    XmlAttribute entityCardID = entity.Attributes["CardID"];
                    /*
                    if (entityCardID == null)
                    {
                        Console.Error.WriteLine("Card had no CardID: {0}", file);
                        continue;
                    }
                    else if (entityCardID.Value.EndsWith("e"))
                    {
                        Console.WriteLine("Skipping effect card: {0}", file);
                        continue;
                    }
                    else if (entityCardID.Value.StartsWith("XXX_"))
                    {
                        Console.WriteLine("Skipping special card type: {0}", file);
                        continue;
                    }
                    else if (char.IsLetter(entityCardID.Value[entityCardID.Value.Length - 1]))
                    {
                        Console.WriteLine("Skipping sub-card: {0}", file);
                        continue;
                    }
                     * */
                    //   <Tag name="CardName" enumID="185" type="String">


                    // Gets localized names
                    XmlNode cardName = document.DocumentElement.SelectSingleNode("//Tag[@name=\"CardName\"]");

                    if (cardName == null)
                    {
                        Console.Error.WriteLine("Card had no CardName tag");
                        continue;
                    }


                    Card card = new Card(entityCardID.Value);
                    card.XmlData = document.ToString();

                    foreach (XmlNode aName in cardName.ChildNodes)
                    {
                        card.SetName(aName.Name, aName.InnerText);
                    }


                    XmlNode cardDescription = document.DocumentElement.SelectSingleNode("//Tag[@name=\"CardTextInHand\"]");

                    if (cardDescription != null)
                    {
                        foreach (XmlNode aDescription in cardDescription.ChildNodes)
                        {
                            card.SetDescription(aDescription.Name, aDescription.InnerText);
                        }
                    }




                    XmlNode attack = document.DocumentElement.SelectSingleNode("//Tag[@name=\"Atk\"]");
                    if (attack != null)
                        card.Attack = int.Parse(attack.Attributes["value"].Value);

                    XmlNode health = document.DocumentElement.SelectSingleNode("//Tag[@name=\"Health\"]");
                    if (health != null)
                        card.Health = int.Parse(health.Attributes["value"].Value);

                    XmlNode cost = document.DocumentElement.SelectSingleNode("//Tag[@name=\"Cost\"]");
                    if (cost != null)
                        card.Cost = int.Parse(cost.Attributes["value"].Value);

                    XmlNode durability = document.DocumentElement.SelectSingleNode("//Tag[@name=\"Durability\"]");
                    if (durability != null)
                        card.Health = int.Parse(durability.Attributes["value"].Value);

                    XmlNode classID = document.DocumentElement.SelectSingleNode("//Tag[@name=\"Class\"]");
                    if (classID != null)
                    {
                        switch (int.Parse(classID.Attributes["value"].Value))
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
                                Console.Error.WriteLine("Unknown class: {0} Class {1}", card.Name, int.Parse(classID.Attributes["value"].Value));
                                break;
                        }
                    }
                    else
                        card.Class = Card.ClassValues.ALL;

                    XmlNode rarity = document.DocumentElement.SelectSingleNode("//Tag[@name=\"Rarity\"]");
                    if (rarity != null)
                    {
                        int value = int.Parse(rarity.Attributes["value"].Value);
                        switch (value)
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


                    XmlNode type = document.DocumentElement.SelectSingleNode("//Tag[@name=\"CardType\"]");
                    if (type != null)
                    {
                        card.Type = int.Parse(type.Attributes["value"].Value);
                        if (card.Type == (int)Card.CardType.HERO || card.Type == (int)Card.CardType.EFFECT) // Heros? 3 included the Hero "Hogger" 0/10 -- 4 may be creatures -- 7 may be weapons (warrior)
                            continue;
                    }

                    XmlNode flavorText = document.DocumentElement.SelectSingleNode("//Tag[@name=\"FlavorText\"]");
                    if (flavorText != null)
                    {

                        foreach (XmlNode aFlavorText in flavorText.ChildNodes)
                        {
                            card.SetFlavorText(aFlavorText.Name, aFlavorText.InnerText);
                        }

                    }

                    cards.Add(card);





                }
                catch (XmlException exception)
                {
                    Console.Error.WriteLine("There was an XML error while parsing a card.");
                    Console.Error.WriteLine(exception);
                }


            }
            Card hearthbot = new Card("CUSTOM_HEARTHBOT");
            hearthbot.SetName("enUS", "HearthBot");
            hearthbot.Cost = 1;
            hearthbot.Health = hearthbot.Attack = 50;
            hearthbot.Class = Card.ClassValues.ALL;
            hearthbot.SetDescription("enUS", @"<b>Battlecry:</b> Destroy all secrets and deal 100 damage to the enemy hero. To get it, go here: https://github.com/aca20031/hsbot/");
            cards.Add(hearthbot);
            return cards;
        }
    }
}
