using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace HSBot
{
    static class Config
    {
        public static XmlDocument Document { get; private set; }

        public static String DataDirectory { get; private set; }
        public static int MaxCardsPerLine { get; private set; }
        public static int MaxCardNameLength { get; private set; }

        public static String IRCHost { get; private set; }
        public static int IRCPort { get; private set; }
        public static String IRCChannel { get; private set; }
        public static String IRCNick { get; private set; }
        public static String IRCName { get; private set; }
        public static String IRCUser { get; private set; }
        public static int IRCReconnectTime { get; private set; }

        public static void Reload()
        {
            XmlDocument doc = new XmlDocument();
            doc.Load("config.xml");
            Document = doc;
            
            // Parses known important options

            DataDirectory = doc.DocumentElement.SelectSingleNode("/config/cards/datadir").InnerText;

            MaxCardsPerLine = int.Parse(doc.DocumentElement.SelectSingleNode("/config/cards/maxcardsperline").InnerText);
            MaxCardNameLength = int.Parse(doc.DocumentElement.SelectSingleNode("/config/cards/maxcardnamelength").InnerText);

            IRCHost = doc.DocumentElement.SelectSingleNode("/config/irc/host").InnerText;
            IRCChannel = doc.DocumentElement.SelectSingleNode("/config/irc/channel").InnerText;
            IRCPort = int.Parse(doc.DocumentElement.SelectSingleNode("/config/irc/port").InnerText);
            IRCReconnectTime = int.Parse(doc.DocumentElement.SelectSingleNode("/config/irc/reconnecttime").InnerText);

            IRCNick = doc.DocumentElement.SelectSingleNode("/config/irc/nick").InnerText;
            IRCUser = doc.DocumentElement.SelectSingleNode("/config/irc/user").InnerText;
            IRCName = doc.DocumentElement.SelectSingleNode("/config/irc/name").InnerText;

        }
    }
}
