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
            FileSystemWatcher dataFileChangeWatcher = new FileSystemWatcher(Path.GetDirectoryName(szCardDataFile), Path.GetFileName(szCardDataFile));
            dataFileChangeWatcher.Changed += (sender, fileSystemEventArgs) =>
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

            dataFileChangeWatcher.EnableRaisingEvents = true;

            // Initialize the client
            Client = new IrcClient();
            Client.Encoding = new System.Text.UTF8Encoding(false);
        }
        
        private DateTime m_startTime = DateTime.Now;
        private Timer m_flowRateTimer;
        private bool m_hasConnected = false;
        private bool m_isConnecting = false;

        /// <summary>
        /// Connects the bot and registers events. Call it only once.
        /// </summary>
        public void StartConnect()
        {
            lock (this)
            {
                if (m_hasConnected)
                {
                    throw new Exception("Only call StartConnect once per instance");
                }
                m_hasConnected = true;
            }

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
                lock (mutexConnect) { if (m_isConnecting) return; m_isConnecting = true; }
              
                Console.WriteLine("Server timed out, reconnecting in 30 seconds...");
                Client.Disconnect();
                System.Threading.Thread.Sleep(30000);

                connectAction(c);
                m_isConnecting = false;
                
            };
            Client.OnDisconnect += (c) =>
            {
               
               m_isConnecting = true; lock (mutexConnect) { if (m_isConnecting) return; m_isConnecting = true; }

                Console.WriteLine("Disconnected from server, reconnecting in 60 seconds...");
                Client.Disconnect();
                System.Threading.Thread.Sleep(60000);
                    
                connectAction(c);
                m_isConnecting = false;
                
            };


            // The point of this is to prevent flooding. This is done by indexing into  to m_flowRateMap with the nickname and incrementing the number value when they talk
            // Everry 1 second, we check to see if FlowRateSeconds has past since the last time they used a command and if so, decrement it
            // When the number reaches a certain threshold, we ignore them
            m_flowRateTimer = new Timer((state) => {
                lock (m_FlowRateMap)
                {
                    // Decrement the message count of everyone who hasn't spoken in FlowRateSeconds
                    // If we get to 0, remove them from the map
                    List<String> keysToRemove = new List<string>();
                    foreach (var kvp in m_FlowRateMap)
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
                        m_FlowRateMap.Remove(key);
                    }
                    keysToRemove.Clear();
                }
            } , null,0,1000);
            
            connectAction(Client);       
        }

        #region Event Handlers
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
        #endregion Event Handlers

        Regex rxUrl = new Regex("(https?://[^ ]+)", RegexOptions.IgnoreCase);
        /// <summary>
        /// Event handler for a privmsg from the server
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="source"></param>
        /// <param name="target"></param>
        /// <param name="message"></param>
        private void OnPrivmsg(IrcClient sender, String source, String target, String message)
        {
            // If its to me (the bot), then respond to source. Otherwise, respond to target (channel)
            String responseTarget = target.Equals(Client.Nick, StringComparison.CurrentCultureIgnoreCase) ?
                ChannelUser.GetNickFromFullAddress(source) :
                target;
            String lowerMessage = message.ToLower();

            // If the lookup request is longer than Config.MaxCardNameLength (default: 30) characters, 
            // it's probably too long to be a card.
            if (lowerMessage.StartsWith("!card ") && message.Length > "!card ".Length && message.Length <= (Config.MaxCardNameLength + "!card ".Length))
            {
                if (CheckFlowRateLimiter(source))
                {
                    FindAndPrintMatch(source, responseTarget, message.Substring("!card ".Length).ToLower());
                    return; // End here so we don't trigger 2 things
                }
			}

            // MatchCardsAndReply does all the work including checking the flow rate limiter
            if (MatchCardsAndReply(source, target, message))
            {
                return; // Stop matching commands
            }

            Match urlMatch = rxUrl.Match(message);
            if (urlMatch.Success)
            {
                String url = urlMatch.Groups[1].Value;
                if (CheckFlowRateLimiter(source))
                {
                    // Async response
                    Task t = HandleUrlAndReply(url, target);
                }
            }
        } // OnPrivmsg

        Regex rxInline = new Regex(@"(?:^|\s)\[([^\]+\]]+)\](?=[^a-zA-Z]|$|e?s[ $,;.!?])");
        /// <summary>
        /// Matches inline cards (based on rxInline) and
        /// </summary>
        /// <param name="source"></param>
        /// <param name="target"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        bool MatchCardsAndReply(String source, String target, String message)
        {
            // If its to me (the bot), then respond to source. Otherwise, respond to target (channel)
            String responseTarget = target.Equals(Client.Nick, StringComparison.CurrentCultureIgnoreCase) ?
                ChannelUser.GetNickFromFullAddress(source) :
                target;
            String lowerMessage = message.ToLower();

            bool bResponded = false;

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
                if (!listMatchedCardNames.Contains(strTriggerText) /* We do it this way to avoid duplicates */)
                {
                    if (!CheckFlowRateLimiter(source))
                    {
                        // Command is handled if we would have responded but the flow rate limited stopped us
                        return true;
                    }
                    listMatchedCardNames.Add(strTriggerText);
                    if (IsNickname(strTriggerText) && !strTriggerText.Equals("HearthBot", StringComparison.CurrentCultureIgnoreCase))
                    {
                        Console.WriteLine("Ignoring inline trigger because it appears to be a nickname: {0}", strTriggerText);
                    }
                    else if (IsTimestamp(strTriggerText))
                    {
                        Console.WriteLine("Ignoring inline trigger because it appears to be a timestamp: {0}", strTriggerText);
                    }
                    else
                    {
                        // Even if nothing matches the card, an error is printed, so consider this responded
                        bResponded = true;
                        FindAndPrintMatch(source, responseTarget, strTriggerText);
                    }
                }
            }
            listMatchedCardNames.Clear();

            return bResponded;
        }

        private Dictionary<String, FlowRateEntry> m_FlowRateMap = new Dictionary<string, FlowRateEntry>();
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
                FlowRateEntry spamFilterLine;
                lock (m_FlowRateMap)
                {   
                    if (!m_FlowRateMap.TryGetValue(strKey, out spamFilterLine))
                    {
                        spamFilterLine = new FlowRateEntry(strKey);
                        lock (spamFilterLine)
                        {
                            m_FlowRateMap[strKey] = spamFilterLine;
                            spamFilterLine.LastUpdated = DateTime.Now;
                        }
                    }
                    lock (spamFilterLine)
                    {
                        ++spamFilterLine.Messages;
                        if (spamFilterLine.Messages > Config.FlowRateMax)
                        {
                            bResult = false;
                            Console.WriteLine("{0} failed flow rate limiter check", source);
                        }
                        else
                        {
                            spamFilterLine.LastUpdated = DateTime.Now;
                            bResult = true;
                        }
                    }
                }
            }

            return bResult;
        }
        /// <summary>
        /// Prints card information to a channel
        /// </summary>
        /// <param name="source">User who sent request</param>
        /// <param name="responseTarget">Place to respond to</param>
        /// <param name="cardname">Query string, e.g. "The Coin" or "Mark of the Wild 2"</param>
        private void FindAndPrintMatch(String source, String responseTarget, String query)
        {
            Debug.Assert(source != null);
            Debug.Assert(responseTarget != null);
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


            var results = LookupCardSet(query, 0.50/*min match*/, 0.50/*boost substrings*/);
            var resultsEnum = results.Reverse();
            if (results.Count == 0)
            {
                Message(responseTarget, "No reasonable matches found.");

            }
            else if (results.Max.MatchPercentage < .75)
            {
                if (results.Count == 1)
                {
                    Message(responseTarget, "This card was not found, did you mean {0}?",
                        results.Max.Item[0].GetmIRCName(Config.ControlCodes));
                    Console.WriteLine("Match percentage: %{0}", results.Max.MatchPercentage * 100);
                }
                else if (results.Count == 2)
                {
                    Message(responseTarget, "This card was not found, did you mean {0} or {1}?",
                        results.Max.Item[0].GetmIRCName(Config.ControlCodes),
                        results.Min.Item[0].GetmIRCName(Config.ControlCodes));
                    Console.WriteLine("Match percentages: %{0} %{1}",
                        results.Max.MatchPercentage * 100,
                        results.Min.MatchPercentage * 100);
                }
                else
                {
                    Message(responseTarget, "This card was not found, did you mean {0}, {1} or {2}?", 
                        results.Max.Item[0].GetmIRCName(Config.ControlCodes),
                        resultsEnum.ElementAt(1).Item[0].GetmIRCName(Config.ControlCodes),
                        resultsEnum.ElementAt(2).Item[0].GetmIRCName(Config.ControlCodes));
                    Console.WriteLine("Match percentages: %{0} %{1} %{2}",
                        results.Max.MatchPercentage * 100,
                        resultsEnum.ElementAt(1).MatchPercentage * 100,
                        resultsEnum.ElementAt(2).MatchPercentage * 100);
                }
            }
            else
            {
                CardSet cs = results.Max.Item;
                if (index < 1 || index > cs.Count)
                    index = 1;
                Card c = cs[index - 1];

                String message = c.GetFullText(Config.ControlCodes);

                if (cs.Count > 1)
                {
                    message = message.Trim() + String.Format(" ({0} of {1} in the set)", index, cs.Count);
                }
                Console.WriteLine("Match is %{0}", results.Max.MatchPercentage * 100);
                Message(responseTarget, message.ToString());
                if (results.Count > 1 && results.Max.MatchPercentage < .90)
                {
                    // If we're not very sure that this is the card we want, let's notice them with a few more
                    StringBuilder noticeMessage = new StringBuilder("Other possible matches:");
                    var enumerator = results.Reverse().GetEnumerator();
                    enumerator.MoveNext(); // Skip first match
                    for (int i = 0; i < 5 && enumerator.MoveNext();++i)
                    {
                        noticeMessage.Append(" ");
                        noticeMessage.Append(enumerator.Current.Item[0].GetmIRCName(Config.ControlCodes));
                    }
                    var sendNoticeTask = Client.SendRawMessageAsync("NOTICE {0} :{1}", ChannelUser.GetNickFromFullAddress(source), noticeMessage.ToString());
                }
            }
        }

        private void Message(String target, String message, params Object[] format)
        {
            var sendMessageTask = Client.SendRawMessageAsync("PRIVMSG {0} :{1}", target, String.Format(message, format));
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
                using (var response = await m_httpClient.GetAsync(url))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(YoutubeData));
                        YoutubeData data = (YoutubeData)serializer.ReadObject(await response.Content.ReadAsStreamAsync());
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

        private class MatchResult<MatchItem> : IComparable<MatchResult<MatchItem>>
        {
            public MatchItem Item { get; set; }
            public double MatchPercentage { get; set; }
            public bool IsValid { get; set; }

            public MatchResult(MatchItem item, double matchPercentage)
            {
                Item = item;
                MatchPercentage = matchPercentage;
                IsValid = true;
            }

            public int CompareTo(MatchResult<MatchItem> other)
            {
                if (MatchPercentage > other.MatchPercentage)
                {
                    return 1;
                }
                else if (MatchPercentage < other.MatchPercentage)
                {
                    return -1;
                }

                // I know this is rediculous but SortedSet doesn't allow 2 "equal" values and for some stupid reason it uses
                // this comparer for that and not equals() so you cannot have 2 non-equal values that are equal on the sorter
                // This is insane. More insane is that SortedList uses KeyValuePairs! Why? I don't know. So this is a workaround.
                return 1;
            }
        }

        /// <summary>
        /// Finds a card set by its name. Employs LevenshteinDistance to find the highest match per word length, boostSubstring for substring matches
        /// </summary>
        /// <param name="searchString"></param>
        /// <param name="minMatchPct">The match % required to be included in the result list</param>
        /// <param name="boostSubstring">If the given cardname is a substring of a card's name, artificially boosts its match percentage by the given amount</param>
        /// <returns>Best matching CardSet or null</returns>
        private SortedSet<MatchResult<CardSet>> LookupCardSet(String searchString, double minMatchPct, double boostSubstring = 0.0)
        {

            var resultList = new SortedSet<MatchResult<CardSet>>();

            searchString = searchString.ToLower();

            lock (m_cardMap)
            {

                // In this loop we calculate a match percentage on a specific card (kvp keys)
                // We track the closest match and return that one.
                // We will compare against all keys so this is omega(the number of cards)
                foreach (var cardMapKvp in m_cardMap.AsEnumerable())
                {
                    if (cardMapKvp.Key.Length == 0)
                        continue;
                    // Match calculation
                    // 1.) Fancy calculation
                    // 2.) If the card name given is a substring of this card name, boost the percentage to allow for lazy matching of long names

                    string testCardName = cardMapKvp.Key.ToLower();

                    double percentMatch = 0;

                    // Explanation:
                    // We find the best matching word in the testCardString for each given word in the search string
                    // When we find a match, we remove that test and search word from future consideration
                    // We give 50% of the match as weight of the search string which matches, and 50% as weight of the test string matched
                    // We repeat this until search words or all test words have been matched off

                    // This map will have an entry for each search word
                    // The result is the set of words in the testCardName sorted by how wel they match the key (search word)
                    var searchWords = new Dictionary<String, SortedSet<MatchResult<String>>>();

                    foreach (var word in searchString.Split(' '))
                        searchWords.Add(word, new SortedSet<MatchResult<String>>());

                    var testCardNameWords = new List<String>(testCardName.Split(' '));

                    foreach (var searchWordsKvp in searchWords)
                    {
                        foreach (var testCardNameWord in testCardNameWords)
                        {
                            // Calculate match to word
                            double match = 1 - (LevenshteinDistance(searchWordsKvp.Key, testCardNameWord) / (double)Math.Max(searchWordsKvp.Key.Length, testCardNameWord.Length));
                            searchWordsKvp.Value.Add(new MatchResult<string>(testCardNameWord,match));
                        }
                        
                    }

                    // Match all the search words off
                    while (searchWords.Count > 0 && testCardNameWords.Count > 0)
                    {
                        KeyValuePair<String, SortedSet<MatchResult<String>>> highest = searchWords.ElementAt(0);
                        foreach (var searchWordsKvp in searchWords)
                        {
                            if (searchWordsKvp.Value.Count == 0)
                            {
                                // This should really never happen
                                Debug.Assert(false);
                                return resultList;
                            }
                            if (searchWordsKvp.Value.Reverse().First((t) => t.IsValid).MatchPercentage > highest.Value.Reverse().First((t) => t.IsValid).MatchPercentage)
                            {
                                highest = searchWordsKvp;
                            }
                        }

                        var maxValue = highest.Value.Reverse().First((t) => t.IsValid);
                        // Remove the match from words so we track that we have already matched this word
                        testCardNameWords.Remove(maxValue.Item);

                        // Remove the matching search word from searchWords so we don't match it against more test words
                        searchWords.Remove(highest.Key);

                        //if (testCardName.Equals("do nothing")) Debugger.Break();

                        // Remove one instance of this test word from the match sets of all the search words so we dont consider it in the next iterations
                        foreach (var searchWordsKvp in searchWords)
                        {
                            searchWordsKvp.Value.Reverse().First<MatchResult<string>> ((test) => { return test.IsValid && test.Item.Equals(maxValue.Item); }).IsValid = false;
                        }

                        // Ignore spaces since we parse on words
                        double percentOfTestStringMatched = ((double)maxValue.Item.Length / testCardName.Replace(" ", "").Length);
                        double percentOfSearchStringMatched = (double)highest.Key.Length / searchString.Replace(" ", "").Length;

                        // A manual knob - how much weight should the (already relative to size) search string match have vs the test string?
                        // A heavier search string means  we assume the input is more likely to be what the user wanted
                        double weightOfSearchString = .75;
                        double weightOfTestString = 1 - weightOfSearchString;

                        /*
                        // Intense debugging
                        if (testCardName.Equals("harvest"))
                        {
                            Console.WriteLine("{0} matches {1} {2}%",highest.Key, maxValue.Item, maxValue.MatchPercentage * 100);
                            Console.WriteLine("\tTest string percent: {0}% has weight {1}%", percentOfTestStringMatched * 100, weightOfSearchString * 100);
                            Console.WriteLine("\tSearch string percent: {0}% has weight {1}%", percentOfSearchStringMatched * 100, weightOfTestString * 100);
                            Console.WriteLine("\tTotal added contribution: {0}%", percentOfSearchStringMatched * maxValue.MatchPercentage * weightOfSearchString + percentOfTestStringMatched * maxValue.MatchPercentage * weightOfTestString);

                        }
                        */

                        percentMatch += percentOfSearchStringMatched * maxValue.MatchPercentage * weightOfSearchString
                                        + percentOfTestStringMatched * maxValue.MatchPercentage * weightOfTestString;

                    }

                    // 2.) Substring
                    // Boost the match percentage if the search string is a subtring of the card name
                    // This allows lazy searches like "rag" to match "ragnarous" even though it is really a small percentage of the whole card
                    // The caller sets the amount by which we boost these matches
                    //if (percentMatch < 1 && testCardName.Contains(searchString)) percentMatch += boostSubstring;

                    if (percentMatch >= minMatchPct)
                    {
                        MatchResult<CardSet> result = new MatchResult<CardSet>(cardMapKvp.Value,percentMatch == 1 ? 1 : Math.Min(0.99, percentMatch));
                        resultList.Add(result);
                    }
                }
            }
            return resultList;
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
