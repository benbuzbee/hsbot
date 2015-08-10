using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO; // For FileSystemWatcher
using benbuzbee.LRTIRC;
using System.Text.RegularExpressions;
using HSBot.Cards;
using System.Threading;
using System.Diagnostics; // For Debug
using System.Net.Http;
using System.Runtime.Serialization.Json; // For json serializer

namespace HSBot
{
    class IRC
    {
        public IrcClient Client { get; private set; }
        private String m_cardDataFilePath;
        public IRC(String szCardDataFile)
        {
            m_cardDataFilePath = szCardDataFile;
            RefreshList();

            Console.WriteLine("Registering for changes to {0}", szCardDataFile); 
            FileSystemWatcher fsw = new FileSystemWatcher(Path.GetDirectoryName(szCardDataFile), Path.GetFileName(szCardDataFile));

            fsw.Changed += (sender, fileSystemEventArgs) =>
                {
                    Console.WriteLine("Detected a change to card data file - refreshing card list.");
                    refresh:
                    lock (m_cardMap)
                    {
                        try
                        {
                            using (var fs = File.OpenRead(fileSystemEventArgs.FullPath)) { }
                            RefreshList();
                        } catch (IOException)
                        {
                            Console.WriteLine("File cannot be read: {0}", fileSystemEventArgs.FullPath);
                            Thread.Sleep(1000);
                            goto refresh;
                        }
                        
                    }
                };

            fsw.EnableRaisingEvents = true;

            // Initialize the client
            Client = new IrcClient();
            Client.Encoding = new System.Text.UTF8Encoding(false);
        }
        
        private DateTime m_startTime = DateTime.Now;
        private Timer m_flowRateTimer;
        private bool m_hasConnected = false;
        private bool m_connecting = false;
        /// <summary>
        /// Connects the bot and registers events. Call it only once.
        /// </summary>
        public void StartConnect()
        {
            if (m_hasConnected)
            {
                throw new Exception("Only call StartConnect once per instance");
            }
            m_hasConnected = true;

            // Register events
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
                            sender.ConnectAsync(Config.IRCNick, Config.IRCUser, Config.IRCName, Config.IRCHost, Config.IRCPort, Config.IRCPass).Wait();
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
                if (!m_connecting)
                {
                    m_connecting = true;
                    Console.WriteLine("Server timed out, reconnecting in 30 seconds...");
                    Client.Disconnect();
                    System.Threading.Thread.Sleep(30000);

                    connectAction(c);
                    m_connecting = false;
                }
            };
            Client.OnDisconnect += (c) =>
            {
                if (!m_connecting)
                {
                    m_connecting = true;
                    Console.WriteLine("Disconnected from server, reconnecting in 60 seconds...");
                    Client.Disconnect();
                    System.Threading.Thread.Sleep(60000);
                    
                    connectAction(c);
                    m_connecting = false;
                }
            };

            m_flowRateTimer = new Timer((state) => {
                lock (m_flowRateMap)
                {
                    // Decrement the message count of everyone who hasn't spoken in FlowRateSeconds
                    // If we get to 0, remove them from the map
                    List<String> keysToRemove = new List<string>();
                    foreach (var kvp in m_flowRateMap)
                    {
                        var line = kvp.Value;
                        lock (line)
                        {
                            if ((DateTime.Now - line.LastUpdated) >= TimeSpan.FromSeconds(Config.FlowRateSeconds))
                            {
                                --line.Messages;
                            }
                            if (line.Messages <= 0)
                            {
                                keysToRemove.Add(kvp.Key);
                            }
                        }
                    }
                    foreach (String key in keysToRemove)
                    {
                        m_flowRateMap.Remove(key);
                    }
                    keysToRemove.Clear();
                }
            } , null,0,1000);
            
            connectAction(Client);       
        }
        private void OnRawMessageReceived(IrcClient sender, String message)
        {
            Console.WriteLine("{0} <-- {1}",DateTime.Now, message);
        }
        private void OnRawMessageSent(IrcClient sender, String message)
        {
            Console.WriteLine("{0} --> {1}", DateTime.Now, message);
        }
        private void OnConnect(IrcClient sender)
        {
            if (Config.OnConnectAction != null)
            {
                sender.SendRawMessageAsync(Config.OnConnectAction).Wait();
            }

            foreach (String channel in Config.IRCChannels)
            {
                sender.SendRawMessageAsync("JOIN {0}", channel).Wait();
            }
 
        }
        Regex rxInline = new Regex(@"(?:^|\s)\[([^\]+\]]+)\](?=[^a-zA-Z]|$|e?s[ $,;.!?])");
        Regex rxUrl = new Regex("(https?://[^ ]+)", RegexOptions.IgnoreCase);
        /// <summary>
        /// Event handlers for a privmsg from the server
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="source"></param>
        /// <param name="target"></param>
        /// <param name="message"></param>
        private void OnPrivmsg(IrcClient sender, String source, String target, String message)
        {
            // If its to me (the bot), then respond to source. Otherwise, respond to target (channel)
            String responseTarget = target.Equals(Client.Nick, StringComparison.CurrentCultureIgnoreCase) ?
                source.Substring(source.IndexOf(":") + 1, source.IndexOf("!") - (source.IndexOf(":") + 1)) : 
                target;

            String lowerMessage = message.ToLower();

            if (lowerMessage.StartsWith("!card ") && message.Length > "!card ".Length && message.Length <= (Config.MaxCardNameLength + "!card ".Length))
            {
                if (CheckFlowRateLimiter(source))
                {
                    // If the lookup request is longer than Config.MaxCardNameLength (default: 30) characters, 
                    // it's probably too long to be a card.
                    FindAndPrintMatch(responseTarget, message.Substring("!card ".Length).ToLower());
                }
			}

            // The check for inlined card triggers
            Match match = rxInline.Match(lowerMessage);
            List<String> listMatchedCardNames = new List<String>();
			for (int i = 0; i < Config.MaxCardsPerLine && match.Success; ++i, match = match.NextMatch())
            {
                String strTriggerText = match.Groups[1].Value;
                if (strTriggerText.Length >= Config.MaxCardNameLength)
                {
                    --i;
                    continue;
                }
                if (!listMatchedCardNames.Contains(strTriggerText) /* No duplicates */ && CheckFlowRateLimiter(source))
                {

                    listMatchedCardNames.Add(strTriggerText);
                    if (IsNickname(strTriggerText) && !strTriggerText.Equals("HearthBot",StringComparison.CurrentCultureIgnoreCase))
                    {
                        Console.WriteLine("Ignoring inline trigger because it appears to be a nickname: {0}", strTriggerText);
                    }
                    else if (IsTimestamp(strTriggerText))
                    {
                        Console.WriteLine("Ignoring inline trigger because it appears to be a timestamp: {0}", strTriggerText);
                    }
                    else
                    {
                        FindAndPrintMatch(responseTarget, strTriggerText);
                    }
                }
            }
            listMatchedCardNames.Clear();

            Match urlMatch = rxUrl.Match(message);
            if (urlMatch.Success)
            {
                String url = urlMatch.Groups[1].Value;
                // Async
                Task t = HandleUrlAndReply(url, target);
            }

            // Auto trigger check
            #region Auto Trigger
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
                    if (CheckFlowRateLimiter(source))
                    {
                        FindAndPrintMatch(responseTarget, max.Key[0].Name);
                    }
                }
                #endregion Auto Trigger
            }
            
        }

        private Dictionary<String, FlowRateEntry> m_flowRateMap = new Dictionary<string, FlowRateEntry>();
        /// <summary>
        /// Given a sender full address, checks to see if he is spam filtered.  This works like a mutex in that it will 
        /// </summary>
        /// <param name="source"></param>
        /// <returns>False if should be blocked, true if allowed</returns>
        private bool CheckFlowRateLimiter(String source)
        {
            bool bResult = false;

            // Try to filter based on host so nick change's don't get around it.  
            String strKey = ChannelUser.GetHostFromFullAddress(source).ToLower();
            if (strKey == null)
            {
                strKey = source.ToLower();
            }

            if (strKey != null)
            {
                // For speedyness, we lock the map to synchronize with the decrement timer but only long enough to get the SpamFilterLine
                // After that we synchronize on the individual line. First we update the last modified time so we don't get cleaned up (probably)
                FlowRateEntry spamFilterLine;
                lock (m_flowRateMap)
                {
                    
                    if (!m_flowRateMap.TryGetValue(strKey, out spamFilterLine))
                    {
                        spamFilterLine = new FlowRateEntry(strKey);
                        m_flowRateMap[strKey] = spamFilterLine;
                        lock (spamFilterLine)
                        {
                            spamFilterLine.LastUpdated = DateTime.Now;
                        }
                    }
                }

                lock (spamFilterLine)
                {
                    if (spamFilterLine.Messages >= Config.FlowRateMax)
                    {
                        bResult = false;
                        Console.WriteLine("{0} failed flow rate limiter check", source);
                    }
                    else
                    {
                        ++spamFilterLine.Messages;
                        spamFilterLine.LastUpdated = DateTime.Now;
                        bResult = true;
                    }
                }

            }

            return bResult;
        }
        /// <summary>
        /// Prints card information to a channel
        /// </summary>
        /// <param name="source">Place to send the privmsg response</param>
        /// <param name="cardname">Query string, e.g. "The Coin" or "Mark of the Wild 2"</param>
        private void FindAndPrintMatch(String source, String query)
        {
            Debug.Assert(source != null);
            Debug.Assert(query != null);

            // Strip surrounding whitespace
            query = query.Trim();

            // Check to see if an index was given
            int index = 0;
            if (query.Length > 0)
            {
                char lastChar = query[query.Length - 1];
                if (char.IsDigit(lastChar))
                {
                    index = lastChar - '0';
                    query = query.Substring(0, query.Length - 1).Trim();
                }

            }


            double matchPct;
            CardSet cs = LookupCardSet(query, out matchPct, 0.5);
            if (cs == null)
            {
                Message(source, "The card was not found.");
            }
            else if (matchPct < .75)
            {
                Message(source, "The card was not found, but {0} is a {1:F2}% match.", cs[0].Name, matchPct * 100);
            }
            else
            {
                if (index < 1 || index > cs.Count)
                    index = 1;
                Card c = cs[index - 1];

                String message = c.GetFullText(Config.ControlCodes);

                if (cs.Count > 1)
                {
                    message = message.Trim() + String.Format(" ({0} of {1} in the set)", index, cs.Count);
                }
                Message(source, message.ToString());
            }
        }

        private void Message(String target, String message, params Object[] format)
        {
            
            var responseTask = Client.SendRawMessageAsync("PRIVMSG {0} :{1}", target, String.Format(message, format));
        }


        HttpClient m_httpClient = new HttpClient();
        /// <summary>
        /// Tries to get the youtube video ID from any URI. This function will make HTTP requests as necessary to follow redirects
        /// </summary>
        /// <param name="uri">A URI to test</param>
        /// <returns>The video ID as a string, or null/empty</returns>
        private async Task<String> GetYoutubeVideoIDFromUriAsync(Uri uri)
        {
            // Test if this is a valid youtube uri with a video ID
            String videoID = GetVideoIDFromYoutubeUri(uri);
            if (videoID != null)
            {
                return videoID;
            }

            // If not, connect with HEAD to see if this redirects to a youtube URI
            m_httpClient.DefaultRequestHeaders.Accept.Clear();
            m_httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/html", 1.0));
            DateTime perfStart = DateTime.Now;
            try {
                Uri finalUri = null;
                using (var response = await m_httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        finalUri = response.RequestMessage.RequestUri;
                    }
                }

                if (finalUri != null)
                {
                    Console.WriteLine("GetYoutubeVideoIDFromUriAsync TTR: {0}", DateTime.Now - perfStart);
                    return GetVideoIDFromYoutubeUri(finalUri);
                }
            }
          
            catch (TaskCanceledException)
            {
                Console.Error.WriteLine("Youtube video ID lookup failed - timeout");
            } 
            catch (Exception e)
            {
                Console.Error.WriteLine("Youtube video ID lookup failed - unhandled exception: {0}", e.Message);
            }
         
            return null;
        }

        /// <summary>
        /// Tries to get the Video ID from a youtube URL.  This will simply parse the absolute URI string
        /// </summary>
        /// <param name="uri"></param>
        /// <returns>The video ID as a string, or null/empty</returns>
        private String GetVideoIDFromYoutubeUri(Uri uri)
        {
            // Only valid when the final destination is youtube.com. TODO: Other TLDs
            bool isYoutube = uri.Host.EndsWith("youtube.com", StringComparison.CurrentCultureIgnoreCase);
            if (isYoutube)
            {
                // Path should look like /watch?v=<ID>
                if (uri.PathAndQuery.StartsWith("/watch"))
                {
                    if (uri.Query != null)
                    {
                        int vStart = uri.Query.IndexOf("v=");
                        if (vStart >= 0 )
                        {
                            vStart += 2;
                            int vEnd = uri.Query.IndexOf('&', vStart);
                            if (vEnd < 0)
                            {
                                vEnd = uri.Query.Length - 1;
                            }
                            return uri.Query.Substring(vStart,vEnd - vStart + 1);
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Given an ID this function will query the Youtube API to get video statistics
        /// </summary>
        /// <param name="id">Video ID to lookup data for</param>
        /// <returns></returns>
        private async Task<YoutubeData> GetYoutubeDataFromIDAsync(String id)
        {
            if (id == null)
            {
                return null;
            }

            String url = String.Format("https://www.googleapis.com/youtube/v3/videos?id={0}&key=AIzaSyDWaA2OoArAjQTHqmN6r9XrpHYNkpKGyGw&part=snippet,contentDetails,statistics,status", id);
            try
            {
                DateTime perfStart = DateTime.Now;
                using (var response = await m_httpClient.GetAsync(url))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(YoutubeData));
                        Console.WriteLine("GetYoutubeDataFromIDAsync TTD: {0}", DateTime.Now - perfStart);
                        YoutubeData data = (YoutubeData)serializer.ReadObject(await response.Content.ReadAsStreamAsync());
                        Console.WriteLine("GetYoutubeDataFromIDAsync TTR: {0}", DateTime.Now - perfStart);
                        return data;
                    }
                }
            }
            catch (TaskCanceledException)
            {
                Console.Error.WriteLine("Youtube data lookup failed - timeout");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Youtube data lookup failed - unhandled exception: {0}", e.Message);
            }
            return null;
        }

        // 
        /// Given a URL, if it is youtube it replies to the target with the appropriate message from the configuration file 
        /// </summary>
        /// <param name="url">URL, hopefully youtube</param>
        /// <param name="target">Destination of the reply message</param>
        private async Task HandleUrlAndReply(String url, String target)
        {
            if (Config.YoutubeFormat == null)
            {
                return;
            }
            try
            {
                Uri uri = new Uri(url);
                String videoId = await GetYoutubeVideoIDFromUriAsync(uri);
                if (!String.IsNullOrEmpty(videoId))
                {
                    try
                    {
                        var data = await GetYoutubeDataFromIDAsync(videoId);
                        if (data != null && data.items.Length > 0)
                        {
                            try
                            {
                                Message(target, Config.YoutubeFormat.FormatWith(data));
                            }
                            catch (Exception)
                            {
                                Console.Error.WriteLine("There was an error displaying youtube data. Please check your youtube formatting string.");
                            }
                        }
                    }
                    catch (HttpRequestException e)
                    {
                        Console.Error.WriteLine("Error connecting to get youtube data: {0}", e.Message);
                    }
                }
            }
            catch (UriFormatException)
            {
                // Ignore poorly formatted URIs
            }
            catch (HttpRequestException)
            {
                Console.Error.WriteLine("There was an error checking the URL {0}", url);
            }
            
        }

        /// <summary>
        /// Finds a card set by its name. Employs LevenshteinDistance to find the highest match per word length, boostSubstring for substring matches
        /// </summary>
        /// <param name="cardname"></param>
        /// <param name="matchPct">The match % returned by the matching algorithm</param>
        /// <param name="boostSubstring">If the given cardname is a substring of a card's name, artificially boosts its match percentage by the given amount</param>
        /// <returns>Best matching CardSet or null</returns>
        private CardSet LookupCardSet(String cardname, out double matchPct, double boostSubstring = 0.0)
        {
            // look for exact match
            CardSet match = null;
            lock (m_cardMap)
            {
                if (m_cardMap.TryGetValue(cardname.ToLower(), out match))
                {
                    matchPct = 100;
                    return match;
                }
            }
            
            // Otherwise search using contains
            CardSet closestMatch = null;
            double closestPercentMatch = 0;
            lock (m_cardMap)
            {
                foreach (var kvp in m_cardMap.AsEnumerable())
                {
                    // Match calculation
                    // 1.) Levenshtien distance over size of the current card we're analyzing
                    // 2.) If the card name given is a substring of this card name, boost the percentage to allow for lazy matching of long names
                    // 3.) If the card name given is a close match to a subset of the cards words (in order) then boost the match
                    double percentMatch = 1 - (LevenshteinDistance(cardname.ToLower(), kvp.Key.ToLower()) / (double)kvp.Key.Length);

                    if (kvp.Key.ToLower().Contains(cardname.ToLower())) percentMatch += boostSubstring; // Abuse system a bit by boosting match rate if its a substring

                    String[] astrWords = kvp.Key.ToLower().Split(' ');
                    for (int iFirstWord = 0; iFirstWord < astrWords.Length; ++iFirstWord)
                    {
                        for (int iLastWord = iFirstWord; iLastWord < astrWords.Length; ++iLastWord)
                        {
                            String strSubPhrase = String.Join(" ", astrWords, iFirstWord, (iLastWord - iFirstWord) + 1);
                            double dSubPhraseMatch = 1 - (LevenshteinDistance(cardname.ToLower(), strSubPhrase) / (double)strSubPhrase.Length);
                            double dSubPhrasePercentOfWhole = (double)strSubPhrase.Length / kvp.Key.Length;
                            percentMatch += dSubPhraseMatch * dSubPhrasePercentOfWhole;
                            
                        }
                    }

                    // If this is a bigger match than the last one we were considering, then use it
                    if (percentMatch > closestPercentMatch)
                    {
                        closestPercentMatch = percentMatch;
                        closestMatch = kvp.Value;
                    }
                }
                matchPct = closestPercentMatch;
                // Since we boost, we may match greater than 100% which doesn't make sense. If the names aren't equal, call it 99%
                if (matchPct >= 100 && !String.Equals(closestMatch[0].Name, cardname, StringComparison.CurrentCultureIgnoreCase))
                {
                    matchPct = 99;
                }
                else if (matchPct > 100)
                {
                    matchPct = 100;
                }
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
        private System.Collections.Generic.Dictionary<String, CardSet> m_cardMap = new System.Collections.Generic.Dictionary<String, CardSet>();
        /// <summary>
        /// Refreshes the cache of cards from the carddata directory
        /// </summary>
        private void RefreshList()
        {

            var cardDefs = CardParser.Extract(m_cardDataFilePath);
            if (!cardDefs.ContainsKey(Config.DefaultLanguage))
            {
                Console.Error.WriteLine("No card definitions found for language {0}", Config.DefaultLanguage);
                return;
            }
            List<Card> list = CardParser.GetCards(cardDefs[Config.DefaultLanguage]);
            lock (m_cardMap)
            {
                m_cardMap.Clear();
                foreach (Card c in list)
                {
                    try
                    {
                        CardSet set;
                        if (!m_cardMap.TryGetValue(c.Name.ToLower(), out set))
                        {
                            set = new CardSet();
                        }
                        int insertPosition = -1;
      
                        // if this card has flavor text and no one else in the set does, he goes first
                        if (c.FlavorText != null)
                        {
                            insertPosition = 0;
                            for (int iLoopIndex = 0; iLoopIndex < set.Count; ++iLoopIndex)
                            {
                                if (set[iLoopIndex].FlavorText != null)
                                {
                                    insertPosition = iLoopIndex + 1;
                                }
                            }
                        }
                        // Same story with description
                        else if (c.Description != null)
                        {
                            insertPosition = 0;
                            for (int iLoopIndex = 0; iLoopIndex < set.Count; ++iLoopIndex)
                            {
                                if (set[iLoopIndex].Description != null)
                                {
                                    insertPosition = iLoopIndex + 1;
                                }
                            }
                        }
                        set.Insert(c, insertPosition);
                        m_cardMap[c.Name.ToLower()] = set;
                    }
                    catch (ArgumentException)
                    {
                        Console.Error.WriteLine("Multiple cards have the name \"{0}\".", c.Name);
                    }
                }
            }
        }
        bool IsTimestamp(String strTest)
        {
            DateTime dt;
            if (DateTime.TryParse(strTest, out dt))
            {
                return true;
            }
            return false;
        }
        bool IsNickname(String strTest)
        {
            foreach (var channel in Client.Channels)
            {
                if (channel.Users.ContainsKey(strTest.ToLower()))
                {
                    return true;
                }
            }
            return false;
        }
    }
    class FlowRateEntry
    {
        public String Key { private set; get; }
        public DateTime LastUpdated { set; get; }
        public int Messages { get; set; }
        public FlowRateEntry(String key)
        {
            Messages = 0;
            LastUpdated = DateTime.Now;
            Key = key;
        }
    }
}
