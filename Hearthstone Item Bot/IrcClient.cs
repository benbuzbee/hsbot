using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Net.Sockets;
using System.IO;

namespace benbuzbee.LRTIRC
{
    /// <summary>
    /// A Client on an IRC server
    /// </summary>
    public class IrcClient
    {

        #region Properties
        /// <summary>
        /// Gets the last exception thrown
        /// </summary>
        public Exception Exception { private set; get; }
        /// <summary>
        /// Gets the underlying TcpClient.  You usually don't want to mess with this.
        /// </summary>
        public TcpClient TCP { private set; get; }
        /// <summary>
        /// Gets the nickname used the last time Connect was called
        /// </summary>
        public String Nick { private set; get; }
        /// <summary>
        /// Gets the username used the last time Connect was called
        /// </summary>
        public String Username { private set; get; }
        /// <summary>
        /// Gets the real name used the last time Connect was called
        /// </summary>
        public String RealName { private set; get; }
        /// <summary>
        /// Gets the host used the last time Connect was called
        /// </summary>
        public String Host { private set; get; }
        /// <summary>
        /// Gets the port used the last time Connect was called
        /// </summary>
        public int Port { private set; get; }
        /// <summary>
        /// Gets the password used the last time Connect was called
        /// </summary>
        public String Password { private set; get; }
        /// <summary>
        /// The last time a message was received from the server
        /// </summary>
        public DateTime LastMessageTime { private set; get; }
        /// <summary>
        /// Set to false until TcpClient connects. Does not necessarily mean we are registered. Set before OnConnect event.
        /// </summary>
        public bool Connected { private set; get; }
        /// <summary>
        /// Gets or sets how long without a message before OnTimeout is raised.  Changes will only take affect on the connect proceeding the change.
        /// </summary>
        public TimeSpan Timeout { set; get; }
        /// <summary>
        /// Sets the Encoding. Defaults to UTF8 without BOM. Will take affect next connect.
        /// </summary>
        public Encoding Encoding { set; get; }
        /// <summary>
        /// Set to false before PASS/NICK/USER strings are sent, the set to TRUE (does not wait on confirmation from server)
        /// </summary>
        public Boolean Registered { private set; get; }
        /// <summary>
        /// Channels which this client is currently in
        /// </summary>
        public IEnumerable<Channel> Channels { get { return _channels.Values; } }
        /// <summary>
        /// Message policies (enum is Flags) enforced on outgoing messages
        /// </summary>
        public OutgoingMessagePolicy OutgoingPolicies { get; set; }

        /// <summary>
        /// A history of messages sent in chronological order.  Internally stored as a Stack.  Maximum size determiend by MaxHistoryStored property.
        /// </summary>
        public IEnumerable<String> OutgoingMessageHistory { 
            get 
            {
                return _outgoingMessageHistory;
            }
        }
           /// <summary>
        /// Message policies (enum is Flags) enforced on outgoing messages
        /// </summary>
        public OutgoingMessagePolicy IncomingPolicies { get; set; }

        /// <summary>
        /// A history of messages sent in chronological order.  Internally stored as a Stack.  Maximum size determiend by MaxHistoryStored property.
        /// </summary>
        public IEnumerable<String> IncomingMessageHistory
        {
            get
            {
                return _incomingMessageHistory;

            }
        }

        /// <summary>
        /// The maximum number of outgoing/incoming messages stored in this client's history.
        /// </summary>
        public int MaxHistoryStored { 
            get
            {
                return _maxHistoryStored;
            } 
            set
            {
                lock (_outgoingMessageHistory)
                {
                    lock (_incomingMessageHistory)
                    {
                        while (value < _outgoingMessageHistory.Count)
                        {
                            _outgoingMessageHistory.RemoveLast();
                        }
                        while (value < _incomingMessageHistory.Count)
                        {
                            _incomingMessageHistory.RemoveLast();
                        }
                        _maxHistoryStored = value;
                    }
                }
            }
        }
        /// <summary>
        /// Information about the server sent on connection
        /// </summary>
        public ServerInfoType ServerInfo { get; private set; }
        #endregion Properties

        #region Private Members
        /// <summary>
        /// Lock when registering the client with the server so nothing interferes
        /// </summary>
        private Object _mutexRegistration = new Object();
        /// <summary>
        /// Used when connecting so there are not concurrent attempts.
        /// </summary>
        private SemaphoreSlim _connectingSemaphore = new SemaphoreSlim(1, 1);
        /// <summary>
        /// Used when writing so there are not concurrent attempts
        /// </summary>
        private SemaphoreSlim _writingSemaphore = new SemaphoreSlim(1, 1);
        private int _maxHistoryStored = 50;
        private LinkedList<String> _outgoingMessageHistory = new LinkedList<String>();
        private LinkedList<String> _incomingMessageHistory = new LinkedList<String>();
        /// <summary>
        /// These are channels which this user is in.  It is a map of channel name -> Channel Object for easy lookup
        /// </summary>
        private IDictionary<String, Channel> _channels = new ConcurrentDictionary<String, Channel>();
        
        private StreamWriter _streamWriter;
        private System.Timers.Timer _timeoutTimer = new System.Timers.Timer();
        private System.Timers.Timer _pingTimer = new System.Timers.Timer();

        /// <summary>
        /// Structure containing general information about the server - only that which is needed by the IrcClient for further action/
        /// For specific info, you should capture it yourself from the appropriate vent
        /// </summary>
        public class ServerInfoType
        {
            private String _PREFIX;
            /// <summary>
            /// The PREFIX sent in numeric 005. Null until then.
            /// </summary>
            public String PREFIX { internal set { _PREFIX = value; PREFIX_modes = value.Substring(1, value.IndexOf(')') - 1); PREFIX_symbols = value.Substring(value.IndexOf(')') + 1); } get { return _PREFIX; } }
            /// <summary>
            /// The modes portion of PREFIX (such as 'o' or 'v'). Null until PREFIX is set.
            /// </summary>
            public String PREFIX_modes;
            /// <summary>
            /// The Symbols portion of PREFIX (such as '@' or '+'); Null until PREFIX is set.
            /// </summary>
            public String PREFIX_symbols;

            private String _CHANMODES;
            /// <summary>
            /// The CHANMODES parameter sent in numeric 005. Null until then.
            /// </summary>
            public String CHANMODES { get { return _CHANMODES; } internal set { _CHANMODES = value; String[] groups = value.Split(','); CHANMODES_list = groups[0]; CHANMODES_parameterAlways = groups[1]; CHANMODES_paramaterToSet = groups[2]; CHANMODES_parameterNever = groups[3]; } }
            /// <summary>
            /// The first group in CHANMODES. These are channel modes that modify a list (bans, invites, etc)
            /// </summary>
            public String CHANMODES_list;
            /// <summary>
            /// The second group in CHANMODES. These are modes that always have a parameter
            /// </summary>
            public String CHANMODES_parameterAlways;
            /// <summary>
            /// The third group in CHANMODES. These are modes that have a parameter when being set, but not unset.
            /// </summary>
            public String CHANMODES_paramaterToSet;
            /// <summary>
            /// The fourth group in CHANMOEDS. These are modes that never have a parameter.
            /// </summary>
            public String CHANMODES_parameterNever;
        };
        /// <summary>
        /// A map of: lower(Channel) -> (Nick -> Prefix List)
        /// </summary>
        private ConcurrentDictionary<string, ConcurrentDictionary<string, StringBuilder>> _channelStatusMap = new ConcurrentDictionary<string, ConcurrentDictionary<string, StringBuilder>>();

        private IrcReader _thread;
        #endregion

        // This region contains event handlers for events.  The last step of which is usually to signal all external event handlers dynamically through the Task system
        #region Internal Events
        /// <summary>
        /// The main event.  Most IRC events originate here - it is called when any newline delimited string is received from the server
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="message"></param>
        private void ieOnMessageReceived(IrcClient sender, String message)
        {
            LastMessageTime = DateTime.Now;
            String[] tokens = message.Split(' ');


            // Scans message for errors and raises error events if it finds one
            ErrorHandler(sender, message);

            // Takes action if the message is a PING
            PingHandler(sender, message);

            // Takes action if the message is a numeric
            NumericHandler(sender, message);

            // Takes action if the message is a PRIVMSG
            PrivmsgHandler(sender, message);

            // Takes action if the message is a JOIN
            
            if (tokens.Length >= 3 && tokens[1].Equals("JOIN"))
            {
                ieOnJoin(sender, tokens[0].Replace(":", ""), tokens[2].Replace(":", ""));
                
            }



            // Signal external event handlers for raw messages
            // Keeps this at the end of the file so that the more specific event, as well as internal events, are all handled first
            if (OnRawMessageReceived != null)
            {
                foreach (var d in OnRawMessageReceived.GetInvocationList())
                {
                    Task.Run(() => d.DynamicInvoke(sender, message));
                }
            }
        }

        /// <summary>
        /// Internal event called when a numeric is received from the server.  It will call more specific numeric handles where they apply before calling the general handler
        /// </summary>
        /// <param name="sender">The IrcClient which received this numeric</param>
        /// <param name="source">Client/server which sent it</param>
        /// <param name="numeric">The numeric</param>
        /// <param name="target"></param>
        /// <param name="other"></param>
        private void ieOnNumericReceived(IrcClient sender, String source, int numeric, String target, String other)
        {

            if (numeric == 1)
            {
                lock (_channels)
                {
                    _channels.Clear(); 
                }
                if (OnConnect != null)
                {
                    foreach (var d in OnConnect.GetInvocationList())
                    {
                        Task.Run(() => d.DynamicInvoke(this));
                    }
                }
            }

            // Parses numeric 5 (List of things the server supports) and calls event with the parsed list
            else if (numeric == 5)
            {
                // Parse parameters
                Dictionary<String, String> parameters = new Dictionary<string, string>();
                String[] tokens = other.Split(' ');
                foreach (String token in tokens)
                {
                    int equalIndex = token.IndexOf('=');
                    if (equalIndex >= 0)
                    {
                        parameters[token.Substring(0, equalIndex)] = token.Substring(equalIndex + 1);
                    }
                    else
                    {
                        parameters[token] = "";
                    }
                }


                // try to update server info struct for values we care about
                String value;
                if (parameters.TryGetValue("PREFIX", out value))
                {
                    sender.ServerInfo.PREFIX = value;
                }

                if (parameters.TryGetValue("CHANMODES", out value))
                {
                    sender.ServerInfo.CHANMODES = value;
                }

                // If the server supports user-host names, request it
                if (parameters.ContainsKey("UHNAMES"))
                {
                    var task = SendRawMessage("PROTOCTL UHNAMES");
                }

                // If the server supports extended names, request it
                if (parameters.ContainsKey("NAMESX"))
                {
                    var task = SendRawMessage("PROTOCTL NAMESX");
                }

                
                // Signal external events for isupport
                if (OnISupport != null)
                {
                    foreach (var d in OnISupport.GetInvocationList())
                    {
                        Task.Run(() => d.DynamicInvoke(this, parameters));
                    }
                }
            }
     
            else if (numeric == 353)
            {
                String[] words = other.Split(' ');
                String channel = words[1];
                String names = other.Substring(other.IndexOf(':') + 1);
                ieOnNames(sender, channel, names);
                
            }
            

        
            


            // Signal external event handlers
            if (OnRfcNumeric != null)
            {
                foreach (var d in OnRfcNumeric.GetInvocationList())
                    Task.Run(() => d.DynamicInvoke(sender, source, numeric, target, other));
            }
        }

        /// <summary>
        /// Called when a NAMES message is received.  Does internal bookkeeping before raising external event handlers
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="channel"></param>
        /// <param name="names">Space delimited list of names.  May include prefixes and user-host values if NAMESX and UHNAMES are supported</param>
        private void ieOnNames(IrcClient sender, String channel, String names)
        {

            // Parses names reply to fill ChannelUser list for Channel
            Channel channelObject = null;
            lock (_channels)
            {
                // Get the channel object or create a new one.  Don't add it to the _channels map if it's new unless we're in it
                if (!_channels.TryGetValue(channel.ToLower(), out channelObject))
                    channelObject = new Channel(channel);

                String[] namesArray = names.Split(' ');
                foreach (String name in namesArray)
                {
                    Debug.Assert(!String.IsNullOrEmpty(name));
                    
                    // if there are symbols (because NAMESX was supported) find the start of the name, otherwise its position 0
                    int nameStart = 0;
                    for (nameStart = 0; sender.ServerInfo.PREFIX_symbols.Contains(name[nameStart]); ++nameStart) ;
                    String justName = name.Substring(nameStart);

                    // Create a ChannelUser for this user if it does not exist in the channel, or get it if it does
                    ChannelUser user;
                    if (!channelObject.Users.TryGetValue(ChannelUser.GetNickFromFullAddress(justName), out user))
                    {
                        user = new ChannelUser(justName, channelObject);
                        channelObject.Users[user.Nick.ToLower()] = user;
                    }

                    // Insert each prefix in the names reply (for NAMESX) into the ChannelUser (InsertPrefix ignores duplicates)
                    for (int i = 0; i < nameStart; ++i)
                        user.InsertPrefix(sender.ServerInfo, name[i]);

                    /// If we are in the NAMES reply for a channel, that means we are in that channel and should make sure it is in our list
                    if (user.Nick.Equals(Nick, StringComparison.CurrentCultureIgnoreCase))
                    {
                        _channels[channelObject.Name.ToLower()] = channelObject;
                    }
                }
            }

            // Signal external events
            if (OnNamesReply != null)
            {
                foreach (var d in OnNamesReply.GetInvocationList())
                {
                    Task.Run(() => d.DynamicInvoke(this, channel, names));
                }
            }
        }

        /// <summary>
        /// Called when JOIN is received from the server (a user joined a channel).  Does internal bookkeeping before raising external events
        /// </summary>
        /// <param name="sender">The IrcClient which received this message</param>
        /// <param name="source">The IRC user who joined</param>
        /// <param name="channellist">Comma delimited list of channels the user has joined</param>
        private void ieOnJoin(IrcClient sender, String source, String channellist)
        {

            // Add the channel to our list if we don't have it
            // Add the user as a ChannelUser
            foreach (String channelName in channellist.Split(','))
            {
                
                lock (_channels)
                {
                    Channel c = null;    
                    if (!_channels.TryGetValue(channelName.ToLower(), out c))
                    {
                        c = new Channel(channelName);
                        _channels[channelName.ToLower()] = c;
                    }
                    
                    ChannelUser u = new ChannelUser(source, c);
                    Debug.Assert(!c.Users.ContainsKey(u.Nick.ToLower()), "Received a JOIN for a user that was already in the ChannelUser list", "User: {0}", u);
                    c.Users[u.Nick.ToLower()] = u;
                    
                }
            }
            // Signal external events

            if (OnRfcJoin != null)
            {
                foreach (var d in OnRfcJoin.GetInvocationList())
                {
                    Task.Run(() => d.DynamicInvoke(sender, source, channellist));
                }
            }
 
        }
        #endregion

        /// <summary>
        /// Represents one connection to one IRC server
        /// </summary>
        public IrcClient()
        {
            ServerInfo = new ServerInfoType();

            Encoding = new System.Text.UTF8Encoding(false);
            Registered = false;
            LastMessageTime = DateTime.Now;
            Timeout = new TimeSpan(0, 2, 0);
            Connected = false;

            _thread = new IrcReader(this);

            // When a message is received on the reader
            _thread.OnRawMessageReceived += (sender, msg) =>
            {

                ieOnMessageReceived(this, msg);

            };

            // When the reader raises an exception
            _thread.OnException += (sender, e) =>
            {

                // Call to clean up resources and set flags
                Disconnect();

                if (OnException != null)
                {
                    foreach (var d in OnException.GetInvocationList())
                    {
                        Task.Run(() => d.DynamicInvoke(this, e));
                    }

                }

                // Call on disconnect when there's an exception in the reading thread
                if (OnDisconnect != null)
                {
                    foreach (var d in OnDisconnect.GetInvocationList())
                    {
                        Task.Run(() => d.DynamicInvoke(this));
                    }

                }

            };

            #region Register Delegates



            OnRawMessageReceived += (sender, message) =>
            {
                String[] tokens = message.Split(' ');
                if (tokens.Length >= 5 && tokens[1].Equals("KICK"))
                {
                    String source = tokens[0].Replace(":", ""), channel = tokens[2], target = tokens[3];
                    String reason = message.Substring(message.IndexOf(':', 1));
                    if (OnRfcKick != null)
                        foreach (var d in OnRfcKick.GetInvocationList())
                            Task.Run(() => d.DynamicInvoke(this, source, target, channel, reason));
                }
            };
            // Catches a part
            OnRawMessageReceived += (sender, message) =>
            {
                String[] tokens = message.Split(' ');
                if (OnRfcPart != null && tokens.Length >= 3 && tokens[1].Equals("PART"))
                {
                    String reason = tokens.Length >= 4 ? message.Substring(message.IndexOf(':', 1)) : null;
                    foreach (var d in OnRfcPart.GetInvocationList())
                        Task.Run(() => d.DynamicInvoke(this, tokens[0].Replace(":", ""), tokens[2], reason));
                }
            };
            OnRawMessageReceived += (sender, message) =>
            {
                String[] tokens = message.Split(' ');
                if (tokens.Length >= 4 && tokens[1].Equals("MODE") && OnRfcMode != null)
                {
                    foreach (var d in OnRfcMode.GetInvocationList())
                        Task.Run(() => d.DynamicInvoke(sender, tokens[0].Replace(":", ""), tokens[2], message.Substring(message.IndexOf(tokens[2]) + tokens[2].Length + 1)));
                }

            };

            // On part, we should remove the nick from the channel. If it's US parting, we should remove the channel from channels
            OnRfcPart += (sender, source, channel, reason) =>
            {
                Channel channelObject = null;
                lock (_channels)
                {
                    _channels.TryGetValue(channel.ToLower(), out channelObject);
                    if (channelObject != null)
                    {
                        if (ChannelUser.GetNickFromFullAddress(source).Equals(Nick, StringComparison.CurrentCultureIgnoreCase))
                            _channels.Remove(channel.ToLower());
                        else
                        {
                            try
                            {
                                channelObject.Users.Remove(ChannelUser.GetNickFromFullAddress(source));
                            }
                            catch (Exception) { }
                        }
                    }
                }
            };

            OnRfcKick += (sender, source, target, channel, reason) =>
            {
                Channel channelObject = null;
                lock (_channels)
                {
                    _channels.TryGetValue(channel.ToLower(), out channelObject);
                }
                Debug.Assert(channelObject != null, "Any channel on which we receive a KICK should be in our channel list", "Channel: {0}", channel);
                
                if (Nick.Equals(target, StringComparison.CurrentCultureIgnoreCase)) // If it's us, remove the channel entirely
                {
                    lock (_channels)
                    {
                        _channels.Remove(channel.ToLower());
                    }
                }
                else // else remove the nick from the channel 
                {
                    try
                    {
                        channelObject.Users.Remove(target.ToLower());
                    }
                    catch (Exception) { } // If the user isn't there...good!
                }
                
            };

            // Updates prefix list for users of a channel when modes are changed
            OnRfcMode += (sender, source, target, modes) =>
            {
                Channel channel = null;
                lock (_channels)
                {
                    _channels.TryGetValue(target.ToLower(), out channel);
                }
                if (channel == null) return;


                String[] tokens = modes.Split(' ');

                // This loop walks through the mode list and keeps track for each one 1.) The mode, 2.) If it is set 3.) If it has a parameter and 4.) The index of the parameter
                // It's purpose is to update the ChannelUser for any user that have their modes affected
                bool isSet = false;
                for (int modeIndex = 0, parameterIndex = 1; modeIndex < tokens[0].Length; ++modeIndex)
                {
                    char mode = tokens[0][modeIndex];
                    if (mode == '+') isSet = true;
                    else if (mode == '-') isSet = false;
                    else if (ServerInfo.CHANMODES_parameterNever.Contains(mode)) continue; // There are no parameters assocaited with this mode, so it can't change a user's prefix
                    else if (ServerInfo.CHANMODES_paramaterToSet.Contains(mode))
                    {
                        if (!isSet) continue; // This mode only has a parameter when being set, so it does not have a parameter in the list if it is not being set
                        else ++parameterIndex; // This mode consumes one of the parameters
                    }
                    else  // These mdoes always associate with a parameter
                    {

                        try
                        {
                            // If it's a user access mode
                            if (ServerInfo.PREFIX_modes.Contains(mode))
                            {
                                ChannelUser user = null;
                                channel.Users.TryGetValue(tokens[parameterIndex].ToLower(), out user);
                                if (user != null)
                                {
                                    char prefix = ServerInfo.PREFIX_symbols[ServerInfo.PREFIX_modes.IndexOf(mode)];
                                    if (isSet)
                                        user.InsertPrefix(ServerInfo, prefix);
                                    else
                                        user.DeletePrefix(prefix);
                                }
                            }

                        }
                        finally
                        {
                            // These modes always have parameters so we need to always increase the index at the end
                            ++parameterIndex;
                        }
                    }

                }

            };

            OnRawMessageReceived += (sender, message) =>
            {
                String[] tokens = message.Split(' ');
                if (tokens.Length >= 3 && tokens[1].Equals("NICK") && OnRfcNick != null)
                {
                    foreach (var d in OnRfcNick.GetInvocationList())
                    {
                        Task.Run(() => d.DynamicInvoke(sender, tokens[0].Replace(":", ""), tokens[2].Replace(":", "")));
                    }
                }

            };

            OnRfcNick += (sender, source, newnick) =>
            {
                //Update ChannelUsers in all my chanels
                String oldnick = ChannelUser.GetNickFromFullAddress(source).ToLower();
                ChannelUser user = null;
                lock (_channels)
                {
                    foreach (Channel c in _channels.Values)
                    {
                        c.Users.TryGetValue(oldnick, out user);
                        if (user != null)
                        {
                            user.Nick = newnick;
                            c.Users.Remove(oldnick);
                            c.Users[newnick] = user;
                        }

                    }
                }

                if (ChannelUser.GetNickFromFullAddress(source) == this.Nick)
                {
                    this.Nick = newnick;
                }
            };

            OnRawMessageReceived += (sender, message) =>
            {
                String[] tokens = message.Split(' ');
                if (tokens.Length >= 3 && OnRfcQuit != null && tokens[1].Equals("QUIT"))
                {
                    foreach (var d in OnRfcQuit.GetInvocationList())
                        Task.Run(() => d.DynamicInvoke(sender, tokens[0].Replace(":", ""), message.Substring(message.IndexOf(":", 1))));
                }
            };
            OnRfcQuit += (sender, source, message) =>
            {
                //Remove this nick from all channels we are in
                String nick = ChannelUser.GetNickFromFullAddress(source);
                lock (_channels)
                {
                    foreach (Channel c in _channels.Values)
                    {
                        try { c.Users.Remove(nick.ToLower()); }
                        catch (Exception) { }

                    }
                }

            };
            #endregion Register Delegates
        }


        #region IO
        /// <summary>
        /// Disconnects client and disposes of streams, disposes of timeout and ping timer and recreates them in an idle state
        /// </summary>
        /// <param name="hasSemaphore">True if we have the _connectingSemaphore before entering</param>
        private void Disconnect(bool hasSemaphore = false)
        {
            try
            {
                if (!hasSemaphore)
                {
                    _connectingSemaphore.Wait();
                }

                lock (_mutexRegistration)
                {
                    lock (_timeoutTimer)
                    {
                        _timeoutTimer.Dispose();
                        _timeoutTimer = new System.Timers.Timer(Timeout.TotalMilliseconds);
                        _timeoutTimer.Elapsed += TimeoutTimerElapsedHandler;
                    }
                    lock (_pingTimer)
                    {
                        _pingTimer.Dispose();
                        _pingTimer = new System.Timers.Timer(Timeout.TotalMilliseconds / 2);
                        _pingTimer.Elapsed += (sender, args) => { var task = SendRawMessage("PING :LRTIRC"); };
                    }

                    Connected = false;
                    Registered = false;
                    if (TCP != null && TCP.Connected)
                    {
                        try
                        {
                            TCP.Close();
                            TCP = null;
                        }
                        catch (Exception) { } // Eat exceptions since this is just an attempt to clean up
                    }
                    if (_streamWriter != null)
                    {
                        try
                        {
                            _streamWriter.Dispose();
                            _streamWriter = null;
                        }
                        catch (Exception) { } // Eat exceptions since this is just an attempt to clean up
                    }

                }
            } 
            finally
            {
                if (!hasSemaphore)
                    _connectingSemaphore.Release();
            }
        }
        /// <summary>
        /// Connects to the server with the provided details
        /// </summary>
        /// <param name="nick">Nickname to use</param>
        /// <param name="user">Username to use</param>
        /// <param name="realname">Real name to use</param>
        /// <param name="host">Host to which to connect</param>
        /// <param name="port">Port on which to connect</param>
        /// <param name="password">Password to send on connect</param>
        /// <returns></returns>
        public async Task Connect(String nick, String user, String realname, String host, int port = 6667, String password = null)
        {
            await _connectingSemaphore.WaitAsync();
            // Dispose of existing connection
            lock (_mutexRegistration)
            {
                Disconnect(true);
                Nick = nick; Username = user; RealName = realname; Host = host; Port = port; Password = password;
                TCP = new TcpClient();
            }
           

            try
            {
                
                Task connectTask = TCP.ConnectAsync(host, port);
                await connectTask;

                if (connectTask.Exception != null)
                {
                    Exception = connectTask.Exception;
                    if (OnException != null)
                        foreach (var d in OnException.GetInvocationList())
                        {
                            var task = Task.Run(() => d.DynamicInvoke(this, connectTask.Exception));
                        }
                    throw connectTask.Exception; // If connect failed
                }

                Connected = true;

                // If connect succeeded

                // Setup reader and writer
                await _writingSemaphore.WaitAsync();
                try
                {
                    _streamWriter = new System.IO.StreamWriter(TCP.GetStream(), Encoding);
                }
                finally
                {
                    _writingSemaphore.Release();
                }
                _thread.Signal();

                RegisterWithServer();

                _timeoutTimer.Start();
                _pingTimer.Start();

            }
            finally
            {
                _connectingSemaphore.Release();
            }


        }
        public async Task<bool> SendRawMessage(String format, params String[] formatParameters)
        {
            return await SendRawMessage(String.Format(format, formatParameters));
        }
        /// <summary>
        /// Sends an EOL-terminated message to the server (\n is appended by this method)
        /// </summary>
        /// <param name="format">Format of message to send</param>
        /// <param name="formatParameters">Format parameters</param>
        /// <returns></returns>
        public async Task<bool> SendRawMessage(String message)
        {
            try
            {
                if ((OutgoingPolicies & OutgoingMessagePolicy.NoDuplicates) == OutgoingMessagePolicy.NoDuplicates)
                {
                    lock (_outgoingMessageHistory)
                    {
                        if (_outgoingMessageHistory.Count > 0 && _outgoingMessageHistory.First((value) => !value.StartsWith("PONG")).Equals(message))
                            return false;
                    }
                }
            } catch (InvalidOperationException)
            {
                // No message history matching this value exists. In this case, continue unimpeded
            }

            try
            {
                await _writingSemaphore.WaitAsync();

                await _streamWriter.WriteLineAsync(message);

                await _streamWriter.FlushAsync();

                lock (_outgoingMessageHistory)
                {
                    _outgoingMessageHistory.AddFirst(message);
                    while (_outgoingMessageHistory.Count > _maxHistoryStored)
                    {
                        _outgoingMessageHistory.RemoveLast();
                    }
                }


            }
            catch (Exception e)
            {
                Exception = e;
                if (OnException != null)
                    foreach (var d in OnException.GetInvocationList())
                    {
                        var task = Task.Run(() => d.DynamicInvoke(this, e));
                    }
                return false;
            }
            finally 
            { 
                _writingSemaphore.Release(); 
            }

            if (OnRawMessageSent != null)
                foreach (var d in OnRawMessageSent.GetInvocationList())
                {
                    var task = Task.Run(() => d.DynamicInvoke(this, message));
                }

            return true;



        }
        #endregion IO


        #region Internal Handlers
        private void TimeoutTimerElapsedHandler(Object sender, System.Timers.ElapsedEventArgs e)
        {
            lock (_timeoutTimer)
            {
                if ((e.SignalTime - LastMessageTime) > Timeout)
                {
                    Disconnect();
                    if (OnTimeout != null)
                        OnTimeout(this);
                }
            }
        }
        private void PrivmsgHandler(IrcClient sender, String message)
        {
            String[] words = message.Split(' ');
            if (words.Length >= 4 && OnRfcPrivmsg != null && words[1].Equals("PRIVMSG", StringComparison.CurrentCultureIgnoreCase))
            {
                OnRfcPrivmsg(this, words[0], words[2], message.Substring(message.IndexOf(":", 1) + 1));
            }
        }
        /// <summary>
        /// A IrcRawMessageHandler for handling ERROR responses
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="message"></param>
        private void ErrorHandler(Object sender, String message)
        {
            if (OnRfcError != null && message.StartsWith("ERROR"))
                OnRfcError(this, message.Substring(message.IndexOf(":") + 1));

        }

        /// <summary>
        /// Registers with the server (sends PASS, NICK, USER)
        /// </summary>
        private void RegisterWithServer()
        {
            lock (_mutexRegistration)
            {
                if (Registered) return;
            }
            System.Threading.Thread.Sleep(1000);
            if (Password != null)
                SendRawMessage("PASS {0}", Password).Wait();
            SendRawMessage("NICK {0}", Nick).Wait();
            SendRawMessage("USER {0} 0 * :{1}", Username, RealName).Wait();

            lock (_mutexRegistration)
            {
                Registered = true;
            }
        }


        private void PingHandler(Object sender, String message)
        {
            if (message.StartsWith("PING"))
            {
                var pongTask = SendRawMessage("PONG {0}", message.Substring("PING ".Length));
            }
        }

        private void NumericHandler(Object sender, String message)
        {
            // FORMAT :<server name> <numeric> <target> :<other>
            var words = message.Split(' ');
            if (words.Length >= 3)
            {
                int numeric;
                if (int.TryParse(words[1], out numeric))
                {
                    ieOnNumericReceived(this, words[0], numeric, words[2], words.Length > 3 ? message.Substring(message.IndexOf(words[2]) + words[2].Length + 1) : null);
                }
            }

        }
        #endregion Internal Handlers


        #region Events and Delegates
        public delegate void IrcExceptionHandler(IrcClient sender, Exception exception);
        /// <summary>
        /// Called when an exception is called by an IRC method, such as failure to connect.
        /// </summary>
        public event IrcExceptionHandler OnException;

        public delegate void IrcRawMessageHandler(IrcClient sender, String message);
        /// <summary>
        /// Called when any EOL-terminated message is received on the TcpClient
        /// </summary>
        public event IrcRawMessageHandler OnRawMessageReceived;
        /// <summary>
        /// Called when any message is successfully sent on the TcpClient
        /// </summary>
        public event IrcRawMessageHandler OnRawMessageSent;

        public delegate void RfcOnErrorHandler(IrcClient sender, String message);
        /// <summary>
        /// Called when ERROR is received from the server
        /// </summary>
        public event RfcOnErrorHandler OnRfcError;

        public delegate void RfcNumericHandler(IrcClient sender, String source, int numeric, String target, String other);
        /// <summary>
        /// Called when an RFC Numeric is received from the server
        /// </summary>
        public event RfcNumericHandler OnRfcNumeric;

        /// <summary>
        /// Called when RFC Numeric 001 is received, to confirm we are both connected and registered.
        /// It is STRONGLY recommended you add a delay to any processing here, especially for channel joining (so we have time to get other numerics)
        /// </summary>
        public event Action<IrcClient> OnConnect;

        /// <summary>
        /// Called when the socket cannot be read from, indicating a disconnect
        /// </summary>
        public event Action<IrcClient> OnDisconnect;

        public delegate void RfcPrivmsgHandler(IrcClient sender, String source, String target, String message);
        /// <summary>
        /// Called when a PRIVMSG is received from the server
        /// </summary>
        public event RfcPrivmsgHandler OnRfcPrivmsg;

        public delegate void RfcNamesReplyHandler(IrcClient sender, String target, String list);


        /// <summary>
        /// Called when a NAMES reply (numeric 353) is received from the server
        /// </summary>
        public event RfcNamesReplyHandler OnNamesReply;

        public delegate void RfcISupport(IrcClient sender, Dictionary<String, String> parameters);
        /// <summary>
        /// Called when an ISupport (numeric 005) is received from the server
        /// </summary>
        public event RfcISupport OnISupport;

        public delegate void RfcJoinHandler(IrcClient sender, String sourceAddress, String channel);
        /// <summary>
        /// Called when a JOIN is received from the server
        /// </summary>
        public event RfcJoinHandler OnRfcJoin;

        public delegate void RfcPartHandler(IrcClient sender, String sourceAddress, String channel, String reason);
        /// <summary>
        /// Called when a PART is received from the server
        /// </summary>
        public event RfcPartHandler OnRfcPart;


        public delegate void RfcKickHandler(IrcClient sender, String source, String target, String channel, String reason);
        /// <summary>
        /// Called when a KICK is received from the server
        /// </summary>
        public event RfcKickHandler OnRfcKick;

        public delegate void RfcModeHandler(IrcClient sender, String source, String target, String modes);
        /// <summary>
        /// Called when a MODE is received from the server
        /// </summary>

        public event RfcModeHandler OnRfcMode;

        public delegate void RfcNickHandler(IrcClient sender, String source, String nick);
        /// <summary>
        /// Triggered when a user changes his nickname (a NICK is received)
        /// </summary>
        public event RfcNickHandler OnRfcNick;

        public delegate void RfcQuitHandler(IrcClient sender, String source, String message);
        /// <summary>
        /// Triggered when a user QUITs (a QUIT is received)
        /// </summary>
        public event RfcQuitHandler OnRfcQuit;

        /// <summary>
        /// Called when LastMessage was more than Timeout ago
        /// </summary>
        public event Action<IrcClient> OnTimeout;
        #endregion Events and Delegates

        /// <summary>
        /// Returns the channel with this name, or null if client is not in the channel
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public Channel GetChannel(String name)
        {
            Channel c = null;
            lock (_channels)
            {
                _channels.TryGetValue(name.ToLower(), out c);
            }
            return c;
        }

    }
    /// <summary>
    /// Represents a Channel on the iRC server
    /// </summary>
    public class Channel
    {
        /// <summary>
        /// The name of this channel
        /// </summary>
        public String Name { get; private set; }
        /// <summary>
        /// A map of lower(nick) -> channeluser, for every user in the channel.  This is essentially just a list of users, but the map makes for an easy lookup
        /// </summary>
        public IDictionary<String, ChannelUser> Users { get { return _users; } }

        private IDictionary<String, ChannelUser> _users = new ConcurrentDictionary<String, ChannelUser>();

        /// <summary>
        /// Gets a user by his name or fulladdress
        /// </summary>
        /// <param name="nameOrFullAddress"></param>
        /// <returns>The channel user, or null if there is no matching user in this channel</returns>
        public ChannelUser GetUser(string nameOrFullAddress)
        {
            ChannelUser result = null;
            lock (_users)
            {
                _users.TryGetValue(ChannelUser.GetNickFromFullAddress(nameOrFullAddress).ToLower(), out result);
            }
            return result;
        }

        internal Channel(String name)
        {

            Name = name;
        }

        /// <summary>
        /// Derived from the Name
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }
        /// <summary>
        /// Channel equality is defined as 2 channels which have the same case-insitive name
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals(object obj)
        {
            return obj is Channel && ((Channel)obj).Name.Equals(Name, StringComparison.CurrentCultureIgnoreCase);
        }
    }
    /// <summary>
    /// Represents a guest of a channel.  Useful for storing mode information - this represents a unique (User, Channel) pair
    /// </summary>
    public class ChannelUser
    {
        /// <summary>
        /// The user's nick
        /// </summary>
        public String Nick { get; set; }
        /// <summary>
        /// The user's username
        /// </summary>
        public String Username { get; set; }
        /// <summary>
        /// The user's host
        /// </summary>
        public String Host { get; set; }
        /// <summary>
        /// The channel this user is in
        /// </summary>
        public Channel Channel { get; private set; }
        /// <summary>
        /// The address of the user in format Nick!Username@Host
        /// </summary>
        public String FullAddress { get { return Nick == null || Username == null || Host == null ? null : String.Format("{0}!{1}@{2}", Nick, Username, Host); } }

        /// <summary>
        /// Checks to see if the user has at least the given prefix SYMBOL (true if his highest prefix is higher or equal to this prefix)
        /// </summary>
        /// <param name="svr"></param>
        /// <param name="prefix"></param>
        /// <returns></returns>
        public bool AtLeast(IrcClient.ServerInfoType svr, char prefix)
        {
            int targetPosition = svr.PREFIX_symbols.IndexOf(prefix);
            if (targetPosition <= 0 || Prefixes.Length == 0) return false;
            int myPosition = svr.PREFIX_symbols.IndexOf(Prefixes[0]);
            if (myPosition < 0) return false;
            return myPosition <= targetPosition;
        }

        /// <summary>
        /// The prefixes this user has in the channel, from most powerful to least
        /// </summary>
        public String Prefixes
        {
            get { lock (_prefixes) { return _prefixes.ToString(); } }
        }

        /// <summary>
        /// The prefixes this user has on this channel
        /// </summary>
        private StringBuilder _prefixes = new StringBuilder("");

        /// <summary>
        /// Creates a new Channel User
        /// </summary>
        /// <param name="nickOrFullAddress">The nick or full address of a user</param>
        /// <param name="channel">The channel this user is in</param>
        public ChannelUser(String nickOrFullAddress, Channel channel)
        {
            if (nickOrFullAddress.Contains('!') && nickOrFullAddress.Contains('@') && nickOrFullAddress.IndexOf('@') > nickOrFullAddress.IndexOf('!'))
            {
                Nick = GetNickFromFullAddress(nickOrFullAddress);
                Username = GetUserFromFullAddress(nickOrFullAddress);
                Host = GetHostFromFullAddress(nickOrFullAddress);

            }
            else Nick = nickOrFullAddress;
        }
        /// <summary>
        /// Gets the nickname portion of a fulladdress, such as NICK!user@host
        /// </summary>
        /// <param name="fulladdress">Full address in format nick!user@host</param>
        /// <returns>The nick portion of the full address, or fulladdress</returns>
        public static String GetNickFromFullAddress(String fulladdress)
        {
            if (!fulladdress.Contains('!'))
                return fulladdress;
            return fulladdress.Substring(0, fulladdress.IndexOf('!')).Replace(":", "");
        }
        /// <summary>
        /// Gets the user portion of a fulladdress, such as nick!USER@host
        /// </summary>
        /// <param name="fulladdress">Full address in format nick!user@host</param>
        /// <returns>The user portion of the fulladdress, or null</returns>
        public static String GetUserFromFullAddress(String fulladdress)
        {
            if (!fulladdress.Contains('!') || !fulladdress.Contains('@') || fulladdress.IndexOf('@') < fulladdress.IndexOf('!'))
                return null;

            int start = fulladdress.IndexOf('!') + 1;
            return fulladdress.Substring(start, fulladdress.IndexOf('@') - start);
        }

        /// <summary>
        /// Gets the nickname portion of a fulladdress, such as nick!user@HOST
        /// </summary>
        /// <param name="fulladdress">Full address in format nick!user@host</param>
        /// <returns>The host portion of the full address, or null</returns>
        public static String GetHostFromFullAddress(String fulladdress)
        {
            if (!fulladdress.Contains('@') || fulladdress.IndexOf('@') == fulladdress.Length)
                return null;
            return fulladdress.Substring(fulladdress.IndexOf('@') + 1);
        }

        /// <summary>
        /// Inserts a prefix (mode symbol) into this client's prefix list for the given channel and svrInfo class (must have PREFIX_symbols set by server)
        /// </summary>
        /// <param name="svrInfo">Struct representing information about the server. Set automatically when we receive ISUPPORT from the server</param>
        /// <param name="prefix">The prefix to insert to this user's prefix list</param>
        internal void InsertPrefix(IrcClient.ServerInfoType svrInfo, char prefix)
        {
            Debug.Assert(svrInfo.PREFIX_symbols != null, "svrInfo.PREFIX_symbols is null - it should have been set when we received ISUPPORT from the server.  It is not possible to maintain a prefix list without this information");
            Debug.Assert(svrInfo.PREFIX_symbols.Contains(prefix), "svrInfo.PREFIX_symbols is non-null but does not contain the prefix that was inserted", "Prefix: {0}", prefix);
            lock (_prefixes)
            {
                if (_prefixes.ToString().Contains(prefix))
                    return;
                else if (_prefixes.Length == 0)
                    _prefixes.Append(prefix);
                else
                {
                    /// Find the first prefix in the current list (newList) whose value is less than this new prefix, and insert at that position
                    /// Or append it to the end if we never find one
                    for (int i = 0; i < _prefixes.Length; ++i)
                    {
                        if (svrInfo.PREFIX_symbols.IndexOf(prefix) < svrInfo.PREFIX_symbols.IndexOf(_prefixes[i]))
                        {
                            _prefixes.Insert(i, prefix);
                            break;
                        }
                        else if (i + 1 == _prefixes.Length) // If we've reached the end and still haven't found one of lower value, then this one belongs at the end
                        {
                            _prefixes.Append(prefix);
                            return;
                        }
                    }
                }

            }
        }
        /// <summary>
        /// Deletes a prefix (mode symbol) from this client's prefix list if it exists in the list
        /// </summary>
        /// <param name="channelName"></param>
        /// <param name="prefix"></param>
        internal void DeletePrefix(char prefix)
        {
            lock (_prefixes)
            {
                int prefixPosition = _prefixes.ToString().IndexOf(prefix);
                if (prefixPosition > 0)
                {
                    _prefixes.Remove(prefixPosition, 1);
                }

            }
        }


    }
    /// <summary>
    /// The integer interpreted by mIRC as a color when following ASCII character 0x03
    /// </summary>
    public enum mIRCColor
    {
        WHITE = 0,
        BLACK = 1,
        DARK_BLUE = 2,
        DARK_GREEN = 3,
        RED = 4,
        DARK_RED = 5,
        DARK_PURPLE = 6,
        ORANGE = 7,
        GOLD = 8,
        GREEN = 9,
        CYAN = 10,
        TEAL = 11,
        BLUE = 12,
        PINK = 13,
        DARK_GRAY = 14,
        GRAY = 15
    }

    /// <summary>
    /// A policy governing an outgoing message.  This applies to any message (line of data) except PINGs.
    /// </summary>
    [Flags]
    public enum OutgoingMessagePolicy
    {
        /// <summary>
        /// When in effect, a message which is the same of the previously sent message will be dropped
        /// </summary>
        NoDuplicates = 0x01
    }

    /// <summary>
    /// Manages the Irc thread which deals directly with the input stream. When signaled it begins trying to read from the input stream for the given IrcClient and raising events.  If it fails, it raises an exception and waits for another signal.
    /// </summary>
    class IrcReader
    {
        public IrcClient Client { get; private set; }
        private SemaphoreSlim _semaphore = new SemaphoreSlim(0, 1);

        /// <summary>
        /// Creates a new reader.  Only makes sense to do this once for each IRC instance.
        /// </summary>
        /// <param name="client"></param>
        public IrcReader(IrcClient client)
        {
            Debug.Assert(client != null);
            Client = client;
            new Thread(ThreadStart).Start();
        }

        /// <summary>
        /// When a message is received on the client's socket
        /// </summary>
        public event Action<IrcReader, String> OnRawMessageReceived;
        /// <summary>
        /// When an exception occurs trying to read from the client's socket
        /// </summary>
        public event Action<IrcReader, Exception> OnException;


        /// <summary>
        /// Thread body
        /// </summary>
        void ThreadStart()
        {
            while (true)
            {
                _semaphore.Wait();

                if (Client == null || Client.TCP == null)
                {
                    continue;
                }

                using (StreamReader reader = new StreamReader(Client.TCP.GetStream()))
                {
                    try
                    {
                        while (Client.TCP.Connected)
                        {
                            String line = reader.ReadLine();
                            if (line != null && OnRawMessageReceived != null)
                            {
                                foreach (var d in OnRawMessageReceived.GetInvocationList())
                                    Task.Run(() => d.DynamicInvoke(this, line));
                            }
                            else if (line == null)
                            {
                                throw new EndOfStreamException();
                            }
                        }
                    }
                    catch (Exception e)
                    {

                        if (OnException != null)
                        {
                            foreach (var d in OnException.GetInvocationList())
                            {
                                var task2 = Task.Run(() => d.DynamicInvoke(this, e));
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Signals the thread to wake up and start checking for message on the associated Client's socket
        /// </summary>
        /// <returns>True if thread woke up, false if thread was not asleep</returns>
        public bool Signal()
        {
            try
            {
                _semaphore.Release();
                return true;
            } catch (SemaphoreFullException)
            {
                return false;
            }
            
        }
    }
}
