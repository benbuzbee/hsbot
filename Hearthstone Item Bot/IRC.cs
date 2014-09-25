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
        private String _cardDataFile;
        public IRC(String cardDataFile)
        {
            _cardDataFile = cardDataFile;
            RefreshList();
            Client = new IrcClient();

            Client.Encoding = new System.Text.UTF8Encoding(false);
            Client.OutgoingPolicies = OutgoingMessagePolicy.NoDuplicates;
            Client.Timeout = TimeSpan.FromSeconds(30);
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

            Object mutexConnect = new Object();

            Action<IrcClient> connectAction = (sender) =>
            {
                lock (mutexConnect)
                {
                    if (sender.Connected) return;
                    while (true)
                    {
                        try
                        {
                            Console.WriteLine("Trying to connect to IRC...");
                            sender.Connect(Config.IRCNick, Config.IRCUser, Config.IRCName, Config.IRCHost, Config.IRCPort, Config.IRCPass).Wait();
                            Console.WriteLine("Connection established");
                            break;
                        }
                        catch (Exception)
                        {
                            Console.WriteLine("Exception while connecting. Trying again in 30 seconds (check your network connection)");
                            System.Threading.Thread.Sleep(30000);
                        }
                    }
                }
            };
            Client.OnTimeout += (c) =>
            {
                Console.WriteLine("Server timed out, reconnecting in 30 seconds...");
                System.Threading.Thread.Sleep(30000);
                connectAction(c);
            };
            Client.OnDisconnect += (c) =>
                {
                    Console.WriteLine("Disconnected from server, reconnecting in 30 seconds...");
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
        Regex regex = new Regex(@"\[([^\]+\]]+)\](?=[^a-zA-Z]|$|s)");
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
                CardSet cs = LookupCardSet(lowerMessage.Substring("!debug ".Length).ToLower(), out m);

                if (cs == null) { Message(responseTarget, "Card not found."); return; }

                foreach (Card c in cs)
                {

                    Console.WriteLine(c.XmlData);


                    String pasteUrl = await DebugPaster.PasteCard(c);



                    if (pasteUrl == null)
                        Message(target, "Paste failed.");
                    else
                        Message(target, "Debug data posted: {0}", pasteUrl);
                }
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
                Dictionary<CardSet, double> matches = new Dictionary<CardSet, double>();
                for (int i = 0; i < lowerWords.Length; ++i)
                {

                    double matchPct;
                    StringBuilder runningString = new StringBuilder(lowerWords[i]);
                    LookupCardSet(runningString.ToString(), out matchPct, 0.0 /* boost */);

                    int backward = i - 1, forward = i + 1;
                    while (true)
                    {
                        double matchPct2;
                        double matchPctLastLoop = matchPct;
                        if (backward >= 0)
                        {
                            
                            LookupCardSet(lowerWords[backward] + " " + runningString.ToString(), out matchPct2, 0.0 /* boost */);
                            if (matchPct2 > matchPct)
                            {
                                matchPct = matchPct2;
                                runningString = new StringBuilder(lowerWords[backward] + " " + runningString.ToString());
                                --backward;
                            }
                        }

                        if (forward < lowerWords.Length)
                        {

                            LookupCardSet(runningString.ToString() + " " + lowerWords[forward], out matchPct2, 0.0);

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
                            matches.Add(LookupCardSet(runningString.ToString(), out matchPct, 0.0), matchPct);
                        }
                        catch (ArgumentException)
                        {
                            // Caught when there already exists a card in the dictionary.  We're OK with that :)
                        }
                    }
                    
                }
                matches.OrderByDescending<KeyValuePair<CardSet, double>, double>((kvp) => kvp.Value);
                for (int i = 0; i < Config.MaxCardsPerLine && matches.Count > 0; ++i)
                {
                    var max = matches.First();
                    matches.Remove(max.Key);
                    LookupCardNameFor(responseTarget, max.Key[0].Name);
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
            System.Diagnostics.Debug.Assert(source != null);
            System.Diagnostics.Debug.Assert(cardname != null);

            cardname = cardname.Trim();

            // Check to see if an index was given
            int index = 0;
            if (cardname.Length > 0)
            {
                char lastChar = cardname[cardname.Length - 1];
                if (char.IsDigit(lastChar))
                {
                    index = lastChar - '0';
                    cardname = cardname.Substring(0, cardname.Length - 1).Trim();
                }

            }


            double m;
            CardSet cs = LookupCardSet(cardname, out m, 0.5);
            if (cs == null || m < .5)
            {
                this.Message(source, "The card was not found.");
            }
            else
            {
                if (index < 1 || index > cs.Count)
                    index = 1;
                Card c = cs[index - 1];

                String message = c.GetFullText();

                if (cs.Count > 1)
                {
                    message = message.Trim() + String.Format(" ({0} of {1} in the set)", index, cs.Count);
                }
                Message(source, message.ToString());
            }
        }



        private void Message(String target, String message, params String[] format)
        {
            
            var responseTask = Client.SendRawMessage("PRIVMSG {0} :{1}", target, String.Format(message, format));
        }

        /// <summary>
        /// Finds a card set by its name. Employs LevenshteinDistance to find the highest match per word length, adding 50% for substrings
        /// </summary>
        /// <param name="cardname"></param>
        /// <returns></returns>
        private CardSet LookupCardSet(String cardname, out double matchPct, double boostSubstring = 0.0)
        {
            // look for exact match
            CardSet match;
            lock (_cards)
            {
                if (_cards.TryGetValue(cardname.ToLower(), out match))
                {
                    matchPct = 100;
                    return match;
                }
            }
            
            // Otherwise search using contains
            CardSet closestMatch = null;
            double closestPercentMatch = 0;
            lock (_cards)
            {
                foreach (var kvp in _cards.AsEnumerable())
                {
                    
                    double percentMatch = 1 - (LevenshteinDistance(cardname.ToLower(), kvp.Key.ToLower()) / (double)kvp.Key.Length);
                    if (kvp.Key.ToLower().Contains(cardname.ToLower())) percentMatch += boostSubstring; // Abuse system a bit by boosting match rate if its a substring
                    if (percentMatch > closestPercentMatch)
                    {
                        closestPercentMatch = percentMatch;
                        closestMatch = kvp.Value;
                    }
                }
                matchPct = closestPercentMatch;
                return closestMatch;
            }
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
        private System.Collections.Generic.Dictionary<String, CardSet> _cards = new System.Collections.Generic.Dictionary<String, CardSet>();
        /// <summary>
        /// Refreshes the cache of cards from the carddata directory
        /// </summary>
        private void RefreshList()
        {

            List<Card> list = CardParser.GetCards(CardParser.Extract(_cardDataFile)["enUS"]);
            lock (_cards)
            {
                _cards.Clear();
                foreach (Card c in list)
                {
                    try
                    {
                        CardSet set;
                        if (!_cards.TryGetValue(c.Name.ToLower(), out set))
                        {
                            set = new CardSet();
                        }
                        int insertPosition = 0;
                        if (char.IsLetter(c.ID[c.ID.Length - 1]))
                            insertPosition = -1;
                        set.Insert(c, insertPosition);
                        _cards[c.Name.ToLower()] = set;
                    }
                    catch (ArgumentException)
                    {
                        Console.Error.WriteLine("Multiple cards have the name \"{0}\".", c.Name);
                    }
                }
            }
        }
    }
}
