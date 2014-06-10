using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using benbuzbee.LRTIRC;
using System.Text.RegularExpressions;

namespace HSBot
{
    class IRC
    {
        public IrcClient Client { get; private set; }
        public IRC()
        {
            
            RefreshList();
            Client = new IrcClient();

            Client.Encoding = new System.Text.UTF8Encoding(false);
            Client.OutgoingPolicies = OutgoingMessagePolicy.NoDuplicates;
        }
        
        private DateTime startTime = DateTime.Now;
        /**
         * Starts a thread that tries to keep the bot connected
         * */
        public void StartConnect()
        {

            
            Client.OnConnect += OnConnect;
            Client.OnRawMessageReceived += OnRawMessageReceived;
            Client.OnRawMessageSent += OnRawMessageSent;
            Client.OnRfcPrivmsg += OnPrivmsg;

            Client.Timeout = new TimeSpan(0,0,0,0,Config.IRCReconnectTime);
            Action<IrcClient> connectAction = (sender) =>
            {
                try
                {
                    sender.Connect(Config.IRCNick, Config.IRCUser, Config.IRCName, Config.IRCHost, Config.IRCPort).Wait();
                } catch (Exception e)
                {
                    Console.WriteLine("Exception while connecting: {0}", e);
                }
            };
            Client.OnTimeout += connectAction;

            connectAction(Client);

                
        }
        private void OnRawMessageReceived(IrcClient sender, String message)
        {
         
            Console.WriteLine("{0} <-- {1}",DateTime.Now - startTime, message);
        }
        private void OnRawMessageSent(IrcClient sender, String message)
        {

            Console.WriteLine("{0} --> {1}", DateTime.Now - startTime, message);
        }
        private void OnConnect(IrcClient sender)
        {
            foreach (String channel in Config.IRCChannels)
                sender.SendRawMessage("JOIN {0}",channel).Wait();
 
        }
        Regex regex = new Regex(@"\[([^\d][^\]]+)\]([^a-zA-Z]|$|s)");
        private async void OnPrivmsg(IrcClient sender, String source, String target, String message)
        {
            // If its to me (the bot), then respond to source. Otherwise, respond to target (channel)
            String responseTarget = target.Equals(Client.Nick, StringComparison.CurrentCultureIgnoreCase) ?
                source.Substring(source.IndexOf(":") + 1, source.IndexOf("!") - (source.IndexOf(":") + 1)) : 
                target; 

            if (message.ToLower().StartsWith("!debug ") && message.Length > "!debug ".Length)
            {
                Cards.Card c = LookupCard(message.Substring("!debug ".Length).ToLower());

                if (c == null) { Message(responseTarget, "Card not found."); return; }

                Console.WriteLine("Source: {0}", c.XmlSource);
                Console.WriteLine(c.XmlData);
               // Message(e.Targets[0].Name, "See terminal for debug data.");


                String pasteUrl = await Cards.DebugPaster.PasteCard(c);



                if (pasteUrl == null)
                    Message(target, "Paste failed.");
                else
                    Message(target, "Debug data posted: {0}", pasteUrl);
                return;
            }

			if (message.ToLower().StartsWith("!card ") && message.Length > "!card ".Length && message.Length <= (Config.MaxCardNameLength + "!card ".Length))
            {
				// If the lookup request is longer than Config.MaxCardNameLength (default: 30) characters, 
				// it's probably too long to be a card.
                LookupCardNameFor(responseTarget, message.Substring("!card ".Length).ToLower());
			}

            Match match = regex.Match(message);

			for (int i = 0; i < Config.MaxCardsPerLine && match.Success; ++i, match = match.NextMatch())
            {
				if (match.Groups[1].Length >= Config.MaxCardNameLength)
                {
                    --i;
                    continue;
                }

                LookupCardNameFor(responseTarget, match.Groups[1].Value);
            }
        }

        private void LookupCardNameFor(String source, String cardname)
        {
            Cards.Card c = LookupCard(cardname);
            if (c == null)
                this.Message(source, "The card was not found.");
            else
                Message(source, c.GetFullText().Replace("<b>", "").Replace("</b>", "").Replace("<i>", "").Replace("</i>", "").Replace(" \\n","."));
        }



        private void Message(String target, String message, params String[] format)
        {
            
            var responseTask = Client.SendRawMessage("PRIVMSG {0} :{1}", target, String.Format(message, format));
        }

        private Cards.Card LookupCard(String cardname)
        {
            // look for exact match
            Cards.Card match;
            if (cards.TryGetValue(cardname.ToLower(), out match))
                return match;
            
            // Otherwise search using contains
            Cards.Card closestMatch = null;
            double closestPercentMatch = 0;
            foreach (Cards.Card c in cards.Values)
            {
                
                double percentMatch = 1 - (LevenshteinDistance(cardname.ToLower(), c.Name.ToLower()) / (double)c.Name.Length);
                if (c.Name.ToLower().Contains(cardname.ToLower())) percentMatch += .5; // Abuse system a bit by boosting match rate if its a substring
                if (percentMatch >= .5 && percentMatch > closestPercentMatch)
                {
                    closestPercentMatch = percentMatch;
                    closestMatch = c;
                }
            }
            return closestMatch;

        }
        public static int LevenshteinDistance(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            // Step 1
            if (n == 0)
            {
                return m;
            }

            if (m == 0)
            {
                return n;
            }

            // Step 2
            for (int i = 0; i <= n; d[i, 0] = i++)
            {
            }

            for (int j = 0; j <= m; d[0, j] = j++)
            {
            }

            // Step 3
            for (int i = 1; i <= n; i++)
            {
                //Step 4
                for (int j = 1; j <= m; j++)
                {
                    // Step 5
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;

                    // Step 6
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            // Step 7
            return d[n, m];
        }
        private System.Collections.Generic.Dictionary<String, Cards.Card> cards = new System.Collections.Generic.Dictionary<String, Cards.Card>();
        private void RefreshList()
        {
            var parser = new Cards.XMLParser(Config.DataDirectory);
            List<Cards.Card> list = parser.GetCards();
            cards.Clear();
            foreach (Cards.Card c in list)
            {
                try
                {
                    cards.Add(c.Name.ToLower(), c);
                }
                catch (ArgumentException)
                {
                    Console.Error.WriteLine("Multiple cards have the name \"{0}\".", c.Name);
                }
            }
        }

    }
}
