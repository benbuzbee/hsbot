using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Text.RegularExpressions;

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
        public static String IRCPass { get; private set; }
        public static int IRCReconnectTime { get; private set; }
        public static String OnConnectAction { get; private set; }
        public static int AutoTriggerMatchRequirement { get; private set; }
        public static bool ControlCodes { get; private set; }
        public static String DefaultLanguage { get; private set; }

        public static int FlowRateMax { get; private set; }
        public static int FlowRateSeconds { get; private set; }

        public static String YoutubeFormat
        {
            get;
            private set;
        }

        public static void Reload()
        {
            XmlDocument doc = new XmlDocument();
            doc.Load("config.xml");
            Document = doc;
            
            // Parses known important options

            MaxCardsPerLine = int.Parse(doc.DocumentElement.SelectSingleNode("/config/spam/maxcardsperline").InnerText);
            MaxCardNameLength = int.Parse(doc.DocumentElement.SelectSingleNode("/config/spam/maxcardnamelength").InnerText);

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
            IRCPass = doc.DocumentElement.SelectSingleNode("/config/irc/pass").InnerText;

            var onConnect = doc.DocumentElement.SelectSingleNode("/config/irc/onconnect");
            if (onConnect != null)
            {
                OnConnectAction = onConnect.InnerText;
            }

            ControlCodes = doc.DocumentElement.SelectSingleNode("/config/irc/nocontrolcodes") == null ? true : false;

            DefaultLanguage = doc.DocumentElement.SelectSingleNode("/config/language/default").InnerText;

            AutoTriggerMatchRequirement = int.Parse(doc.DocumentElement.SelectSingleNode("/config/autotrigger/matchrequirement").InnerText);

            FlowRateMax = int.Parse(doc.DocumentElement.SelectSingleNode("/config/spam/flowrate/max").InnerText);
            FlowRateSeconds = int.Parse(doc.DocumentElement.SelectSingleNode("/config/spam/flowrate/seconds").InnerText);

            var youtube = doc.DocumentElement.SelectSingleNode("/config/youtube");
            if (youtube != null)
            {
                YoutubeFormat = youtube.SelectSingleNode("format").InnerText;
            }

        }

        public static string FormatWith(this string format, object source)
        {
            return FormatWith(format, null, source);
        }

        public static string FormatWith(this string format, IFormatProvider provider, object source)
        {
            if (format == null)
                throw new ArgumentNullException("format");

            Regex r = new Regex(@"(?<start>\{)+(?<property>[\w\.\[\]]+)(?<format>:[^}]+)?(?<end>\})+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

            List<object> values = new List<object>();
            string rewrittenFormat = r.Replace(format, delegate(Match m)
            {
                Group startGroup = m.Groups["start"];
                Group propertyGroup = m.Groups["property"];
                Group formatGroup = m.Groups["format"];
                Group endGroup = m.Groups["end"];

                values.Add((propertyGroup.Value == "0")
                  ? source
                  : System.Web.UI.DataBinder.Eval(source, propertyGroup.Value));

                return new string('{', startGroup.Captures.Count) + (values.Count - 1) + formatGroup.Value
                  + new string('}', endGroup.Captures.Count);
            });

            return string.Format(provider, rewrittenFormat, values.ToArray());
        }
    }
}
