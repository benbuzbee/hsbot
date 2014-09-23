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
        /// <summary>
        /// The XmlDocument for the config
        /// </summary>
        public static XmlDocument Document { get; private set; }
        public static int MaxCardsPerLine { get; private set; }
        public static int MaxCardNameLength { get; private set; }

        public static String IRCHost { get; private set; }
        public static int IRCPort { get; private set; }
        private static List<String> channels = new List<String>();
        public static String[] IRCChannels { get { return channels.ToArray(); } }
        public static String IRCNick { get; private set; }
        public static String IRCName { get; private set; }
        public static String IRCUser { get; private set; }
        public static int IRCReconnectTime { get; private set; }
        public static String OnConnectAction { get; private set; }
        public static int AutoTriggerMatchRequirement { get; private set; }

        public static void Reload()
        {
            XmlDocument doc = new XmlDocument();
            doc.Load("config.xml");
            Document = doc;
            
            // Parses known important options

            MaxCardsPerLine = int.Parse(doc.DocumentElement.SelectSingleNode("/config/cards/maxcardsperline").InnerText);
            MaxCardNameLength = int.Parse(doc.DocumentElement.SelectSingleNode("/config/cards/maxcardnamelength").InnerText);

            IRCHost = doc.DocumentElement.SelectSingleNode("/config/irc/host").InnerText;

            channels.Clear();
            foreach (var channelNode in doc.DocumentElement.SelectNodes("/config/irc/channel"))
            {
                channels.Add(((XmlNode)channelNode).InnerText);
            }
            //IRCChannel = doc.DocumentElement.SelectSingleNode("/config/irc/channel").InnerText;


            IRCPort = int.Parse(doc.DocumentElement.SelectSingleNode("/config/irc/port").InnerText);
            IRCReconnectTime = int.Parse(doc.DocumentElement.SelectSingleNode("/config/irc/reconnecttime").InnerText);

            IRCNick = doc.DocumentElement.SelectSingleNode("/config/irc/nick").InnerText;
            IRCUser = doc.DocumentElement.SelectSingleNode("/config/irc/user").InnerText;
            IRCName = doc.DocumentElement.SelectSingleNode("/config/irc/name").InnerText;

            var onConnect = doc.DocumentElement.SelectSingleNode("/config/irc/onconnect");
            if (onConnect != null)
            {
                OnConnectAction = onConnect.InnerText;
            }

            AutoTriggerMatchRequirement = int.Parse(doc.DocumentElement.SelectSingleNode("/config/autotrigger/matchrequirement").InnerText);

        }
    }
}
