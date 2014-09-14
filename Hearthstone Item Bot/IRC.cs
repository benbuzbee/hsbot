using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using benbuzbee.LRTIRC;
using System.Text.RegularExpressions;
using HSBot.Cards;

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

        /// <summary>
        /// Connects the bot and registers events
        /// </summary>
        public void StartConnect()
        {

            
            Client.OnConnect += OnConnect;
            Client.OnRawMessageReceived += OnRawMessageReceived;
            Client.OnRawMessageSent += OnRawMessageSent;
            Client.OnRfcPrivmsg += OnPrivmsg;

            Client.Timeout = new TimeSpan(0,0,0,0,Config.IRCReconnectTime);
            Action<IrcClient> connectAction = (sender) =>
            {
                while (true)
                {
                    try
                    {
                        sender.Connect(Config.IRCNick, Config.IRCUser, Config.IRCName, Config.IRCHost, Config.IRCPort).Wait();
                        Console.WriteLine("Connection established");
                        break;
                    }
                    catch (Exception)
                    {
                        Console.WriteLine("Exception while connecting. Trying again in 30 seconds...");
                        System.Threading.Thread.Sleep(30000);
                    }
                }
            };
            Client.OnTimeout += (c) =>
            {
                Console.WriteLine("Reconnecting in 30 seconds...");
                System.Threading.Thread.Sleep(30000);
                connectAction(c);
            };
            Client.OnDisconnect += (c) =>
                {
                    Console.WriteLine("Reconnecting in 30 seconds...");
                    System.Threading.Thread.Sleep(30000);
                    connectAction(c);
                };

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
            if (Config.OnConnectAction != null)
            {
                sender.SendRawMessage(Config.OnConnectAction).Wait();
            }

            foreach (String channel in Config.IRCChannels)
            {
                sender.SendRawMessage("JOIN {0}", channel).Wait();
            }
 
        }
        Regex regex = new Regex(@"\[([^\d\]]+)\](?=[^a-zA-Z]|$|s)");
        private async void OnPrivmsg(IrcClient sender, String source, String target, String message)
        {
            // If its to me (the bot), then respond to source. Otherwise, respond to target (channel)
            String responseTarget = target.Equals(Client.Nick, StringComparison.CurrentCultureIgnoreCase) ?
                source.Substring(source.IndexOf(":") + 1, source.IndexOf("!") - (source.IndexOf(":") + 1)) : 
                target;

            String lowerMessage = message.ToLower();

            if (lowerMessage.StartsWith("!debug ") && message.Length > "!debug ".Length)
            {
                double m;
                Card c = LookupCard(lowerMessage.Substring("!debug ".Length).ToLower(), out m);

                if (c == null) { Message(responseTarget, "Card not found."); return; }

                Console.WriteLine("Source: {0}", c.XmlSource);
                Console.WriteLine(c.XmlData);
               // Message(e.Targets[0].Name, "See terminal for debug data.");


                String pasteUrl = await DebugPaster.PasteCard(c);



                if (pasteUrl == null)
                    Message(target, "Paste failed.");
                else
                    Message(target, "Debug data posted: {0}", pasteUrl);
                return;
            }

            else if (lowerMessage.StartsWith("!card ") && message.Length > "!card ".Length && message.Length <= (Config.MaxCardNameLength + "!card ".Length))
            {
				// If the lookup request is longer than Config.MaxCardNameLength (default: 30) characters, 
				// it's probably too long to be a card.
                LookupCardNameFor(responseTarget, message.Substring("!card ".Length).ToLower());
			}

            // The check for manually triggered cards
            Match match = regex.Match(lowerMessage);
			for (int i = 0; i < Config.MaxCardsPerLine && match.Success; ++i, match = match.NextMatch())
            {
				if (match.Groups[1].Length >= Config.MaxCardNameLength)
                {
                    --i;
                    continue;
                }

                LookupCardNameFor(responseTarget, match.Groups[1].Value);
            }
            
            // Auto trigger check

            double atThreshold = Config.AutoTriggerMatchRequirement / 100.0;
            if (atThreshold > 0)
            {
                string[] lowerWords = lowerMessage.Split(' ');
                Dictionary<Card, double> matches = new Dictionary<Card, double>();
                for (int i = 0; i < lowerWords.Length; ++i)
                {

                    double matchPct;
                    StringBuilder runningString = new StringBuilder(lowerWords[i]);
                    LookupCard(runningString.ToString(), out matchPct, 0.0 /* boost */);

                    int backward = i - 1, forward = i + 1;
                    while (true)
                    {
                        double matchPct2;
                        double matchPctLastLoop = matchPct;
                        if (backward >= 0)
                        {
                            
                            LookupCard(lowerWords[backward] + " " + runningString.ToString(), out matchPct2, 0.0 /* boost */);
                            if (matchPct2 > matchPct)
                            {
                                matchPct = matchPct2;
                                runningString = new StringBuilder(lowerWords[backward] + " " + runningString.ToString());
                                --backward;
                            }
                        }

                        if (forward < lowerWords.Length)
                        {

                            LookupCard(runningString.ToString() + " " + lowerWords[forward], out matchPct2, 0.0);

                            if (matchPct2 > matchPct)
                            {

                                matchPct = matchPct2;
                                runningString.Append(" ");
                                runningString.Append(lowerWords[forward]);
                                forward++;
                            }

                        }

                        if (matchPct == matchPctLastLoop) break;

                        matchPctLastLoop = matchPct;

                    }


                    if (matchPct >= atThreshold)
                    {
                        try
                        {
                            matches.Add(LookupCard(runningString.ToString(), out matchPct, 0.0), matchPct);
                        }
                        catch (ArgumentException)
                        {
                            // Caught when there already exists a card in the dictionary.  We're OK with that :)
                        }
                    }
                    
                }
                matches.OrderByDescending<KeyValuePair<Card, double>, double>((kvp) => kvp.Value);
                for (int i = 0; i < Config.MaxCardsPerLine && matches.Count > 0; ++i)
                {
                    KeyValuePair<Card, double> max = matches.First();
                    matches.Remove(max.Key);
                    LookupCardNameFor(responseTarget, max.Key.Name);
                }

            }
        }
        /// <summary>
        /// Prints card information to a channel
        /// </summary>
        /// <param name="source"></param>
        /// <param name="cardname"></param>
        private void LookupCardNameFor(String source, String cardname)
        {
            double m;
            Card c = LookupCard(cardname, out m, 0.5);
            if (c == null || m < .5)
                this.Message(source, "The card was not found.");
            else
                Message(source, c.GetFullText().Replace("<b>", "").Replace("</b>", "").Replace("<i>", "").Replace("</i>", "").Replace("\\n",". "));
        }



        private void Message(String target, String message, params String[] format)
        {
            
            var responseTask = Client.SendRawMessage("PRIVMSG {0} :{1}", target, String.Format(message, format));
        }

        /// <summary>
        /// Finds a card by its name. Employs LevenshteinDistance to find the highest match per word length, adding 50% for substrings
        /// </summary>
        /// <param name="cardname"></param>
        /// <returns></returns>
        private Card LookupCard(String cardname, out double matchPct, double boostSubstring = 0.0)
        {
            // look for exact match
            Card match;
            if (cards.TryGetValue(cardname.ToLower(), out match))
            {
                matchPct = 100;
                return match;
            }
            
            // Otherwise search using contains
            Card closestMatch = null;
            double closestPercentMatch = 0;
            foreach (Card c in cards.Values)
            {
                
                double percentMatch = 1 - (LevenshteinDistance(cardname.ToLower(), c.Name.ToLower()) / (double)c.Name.Length);
                if (c.Name.ToLower().Contains(cardname.ToLower())) percentMatch += boostSubstring; // Abuse system a bit by boosting match rate if its a substring
                if (percentMatch > closestPercentMatch)
                {
                    closestPercentMatch = percentMatch;
                    closestMatch = c;
                }
            }
            matchPct = closestPercentMatch;
            return closestMatch;

        }

        /// <summary>
        /// Stolen LevenshteinDistance implementation
        /// </summary>
        /// <param name="s"></param>
        /// <param name="t"></param>
        /// <returns></returns>
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
        private System.Collections.Generic.Dictionary<String, Card> cards = new System.Collections.Generic.Dictionary<String, Card>();
        /// <summary>
        /// Refreshes the cache of cards from the carddata directory
        /// </summary>
        private void RefreshList()
        {
            var parser = new Cards.XMLParser(Config.DataDirectory);
            List<Card> list = parser.GetCards();
            cards.Clear();
            foreach (Card c in list)
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
