using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;

using System.Net.Sockets;

namespace benbuzbee.LRTIRC
{
    public class IrcClient
    {

        #region Properties
        public Exception Exception { private set; get; }
        public TcpClient TCP { private set; get; }
        public String Nick { private set; get; }
        public String Username { private set; get; }
        public String RealName { private set; get; }
        public String Host { private set; get; }
        public int Port { private set; get; }
        public String Password { private set; get; }
        public DateTime LastMessage { private set; get; }
        /// <summary>
        /// Set to false until TcpClient connects. Does not necessarily mean we are registered. Set before OnConnect event.
        /// </summary>
        public bool Connected { private set; get; }
        /// <summary>
        /// How long without a message before we time out. Will take affect next connect
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

        public IEnumerable<Channel> Channels { get { return channels.Values; } }

        #endregion Properties

        #region Private Members
        private Object registrationLock = new Object();
        /// <summary>
        /// These are channels which this user is in
        /// </summary>
        private IDictionary<String, Channel> channels = new ConcurrentDictionary<String, Channel>();
        private System.IO.StreamReader streamReader;
        private System.IO.StreamWriter streamWriter;
        private System.Timers.Timer timeoutTimer = new System.Timers.Timer();
        public class ServerInfoType
        {
            private String _PREFIX;
            /// <summary>
            /// The PREFIX sent in numeric 005. Null until then.
            /// </summary>
            //
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

        public ServerInfoType ServerInfo { get; private set; }
        /// <summary>
        /// A map of: lower(Channel) -> (Nick -> Prefix List)
        /// </summary>
        private ConcurrentDictionary<string, ConcurrentDictionary<string, StringBuilder>> channelStatusMap = new ConcurrentDictionary<string, ConcurrentDictionary<string, StringBuilder>>();
        #endregion



        public IrcClient()
        {
            ServerInfo = new ServerInfoType();

            Encoding = new System.Text.UTF8Encoding(false);
            Registered = false;
            LastMessage = DateTime.Now;
            Timeout = new TimeSpan(0, 5, 0);
            Connected = false;

            #region Register Delegates

            OnRawMessageReceived += ErrorHandler; // This one looks for ERROR messages
            OnRawMessageReceived += PingHandler;
            OnRawMessageReceived += NumericHandler;
            OnRawMessageReceived += PrivmsgHandler;
            OnRfcNumeric += (sender, source, numeric, target, other) => { if (numeric == 1 && OnConnect != null) OnConnect(this); };
            OnRfcNumeric += (sender, source, numeric, target, other) =>
            {
                if (numeric == 353 && OnNamesReply != null)
                {
                    String[] words = other.Split(' ');
                    String channel = words[1];
                    OnNamesReply(this, channel, other.Substring(other.IndexOf(':') + 1));
                }
            };

            OnRfcNumeric += (sender, source, numeric, target, other) =>
            {
                if (numeric == 5 && OnISupport != null)
                {
                    Dictionary<String, String> parameters = new Dictionary<string, string>();
                    String[] tokens = other.Split(' ');
                    foreach (String token in tokens)
                    {
                        int equalIndex = token.IndexOf('=');
                        if (equalIndex >= 0) parameters[token.Substring(0, equalIndex)] = token.Substring(equalIndex + 1);
                        else
                            parameters[token] = "";
                    }
                    OnISupport(this, parameters);
                }
            };
            OnConnect += (sender) => { lock (channels) { channels.Clear(); } };
            OnISupport += (sender, parameters) =>
            {
                try
                {

                    sender.ServerInfo.PREFIX = parameters["PREFIX"];
                }
                catch (KeyNotFoundException) { };

                try
                {
                    sender.ServerInfo.CHANMODES = parameters["CHANMODES"];
                }
                catch (KeyNotFoundException) { };
                if (parameters.ContainsKey("UHNAMES"))
                    SendRawMessage("PROTOCTL UHNAMES");


                if (parameters.ContainsKey("NAMESX"))
                    SendRawMessage("PROTOCTL NAMESX");

            };

            OnRawMessageReceived += (source, message) =>
            {
                String[] tokens = message.Split(' ');
                if (OnRfcJoin != null && tokens.Length >= 3 && tokens[1].Equals("JOIN"))
                {
                    OnRfcJoin(this, tokens[0].Replace(":", ""), tokens[2].Replace(":", ""));
                }
            };

            OnRfcJoin += (sender, source, channellist) =>
            {
                /// If the user joining is me, add it to the channel list.
                /// If we have a channel already, add the guy to it.


                foreach (String channelName in channellist.Split(','))
                {



                    Channel c = null;
                    lock (channels)
                    {
                        channels.TryGetValue(channelName.ToLower(), out c);
                        if (c == null && ChannelUser.GetNickFromFullAddress(source).Equals(Nick, StringComparison.CurrentCultureIgnoreCase))
                        {
                            c = new Channel(channelName);
                            channels[channelName.ToLower()] = c;
                        }

                        if (c != null)
                        {
                            ChannelUser u = new ChannelUser(source, c);
                            c.Users[u.Nick.ToLower()] = u;
                        }
                    }
                }


            };

            OnRawMessageReceived += (sender, message) =>
            {
                String[] tokens = message.Split(' ');
                if (tokens.Length >= 5 && tokens[1].Equals("KICK"))
                {
                    String source = tokens[0].Replace(":", ""), channel = tokens[2], target = tokens[3];
                    String reason = message.Substring(message.IndexOf(':', 1));
                    if (OnRfcKick != null)
                        OnRfcKick(this, source, target, channel, reason);
                }
            };
            // Catches a part
            OnRawMessageReceived += (sender, message) =>
            {
                String[] tokens = message.Split(' ');
                if (OnRfcPart != null && tokens.Length >= 3 && tokens[1].Equals("PART"))
                {
                    String reason = tokens.Length >= 4 ? message.Substring(message.IndexOf(':', 1)) : null;
                    OnRfcPart(this, tokens[0].Replace(":", ""), tokens[2], reason);
                }
            };
            OnRawMessageReceived += (sender, message) =>
            {
                String[] tokens = message.Split(' ');
                if (tokens.Length >= 4 && tokens[1].Equals("MODE") && OnRfcMode != null)
                {
                    OnRfcMode(sender, tokens[0].Replace(":", ""), tokens[2], message.Substring(message.IndexOf(tokens[2]) + tokens[2].Length + 1));
                }

            };

            // On part, we should remove the nick from the channel. If it's US parting, we should remove the channel from channels
            OnRfcPart += (sender, source, channel, reason) =>
            {
                Channel channelObject = null;
                lock (channels)
                {
                    channels.TryGetValue(channel.ToLower(), out channelObject);
                    if (channelObject != null)
                    {
                        if (ChannelUser.GetNickFromFullAddress(source).Equals(Nick, StringComparison.CurrentCultureIgnoreCase))
                            channels.Remove(channel.ToLower());
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
                channels.TryGetValue(channel.ToLower(), out channelObject);
                if (channelObject != null)
                {

                    if (Nick.Equals(target, StringComparison.CurrentCultureIgnoreCase)) // If it's us, remove the channel
                        channels.Remove(channel.ToLower());
                    else // else remove the nick from the channel 
                    {
                        try
                        {
                            channelObject.Users.Remove(target.ToLower());
                        }
                        catch (Exception) { }
                    }
                }
            };
            OnRawMessageReceived += (source, message) => { LastMessage = DateTime.Now; };
            OnNamesReply += NamesReplyHandler;

            // Updates prefix list for users of a channel when modes are changed
            OnRfcMode += (sender, source, target, modes) =>
            {
                Channel channel = null;
                channels.TryGetValue(target.ToLower(), out channel);
                if (channels == null) return;


                String[] tokens = modes.Split(' ');

                bool isSet = false;
                for (int modeIndex = 0, parameterIndex = 1; modeIndex < tokens[0].Length; ++modeIndex)
                {
                    char mode = tokens[0][modeIndex];
                    if (mode == '+') isSet = true;
                    else if (mode == '-') isSet = false;
                    else if (ServerInfo.CHANMODES_parameterNever.Contains(mode)) continue;
                    else if (ServerInfo.CHANMODES_paramaterToSet.Contains(mode))
                    {
                        if (isSet) ++parameterIndex;
                        else continue;
                    }
                    else
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
                                        user.InsertPrefix(ServerInfo, target, prefix);
                                    else
                                        user.DeletePrefix(target, prefix);
                                }
                            }

                        }
                        catch (Exception) { }
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
                    OnRfcNick(sender, tokens[0].Replace(":", ""), tokens[2].Replace(":", ""));

            };

            OnRfcNick += (sender, source, newnick) =>
            {
                String oldnick = ChannelUser.GetNickFromFullAddress(source).ToLower();
                ChannelUser user = null;
                foreach (Channel c in channels.Values)
                {
                    c.Users.TryGetValue(oldnick, out user);
                    if (user != null)
                    {
                        user.Nick = newnick;
                        c.Users.Remove(oldnick);
                        c.Users[newnick] = user;
                    }

                }
            };

            OnRawMessageReceived += (sender, message) =>
            {
                String[] tokens = message.Split(' ');
                if (tokens.Length >= 3 && OnRfcQuit != null && tokens[1].Equals("QUIT"))
                {
                    OnRfcQuit(sender, tokens[0].Replace(":", ""), message.Substring(message.IndexOf(":", 1)));
                }
            };
            OnRfcQuit += (sender, source, message) =>
            {
                String nick = ChannelUser.GetNickFromFullAddress(source);
                foreach (Channel c in channels.Values)
                {
                    try { c.Users.Remove(nick.ToLower()); }
                    catch (Exception) { }

                }

            };
            #endregion Register Delegates
        }

        #region IO
        /// <summary>
        /// Disconnects client and disposes of streams. Also stops timeout-check timer.
        /// </summary>
        public void Disconnect()
        {
            lock (registrationLock)
            {
                Connected = false;
                Registered = false;
                if (TCP != null && TCP.Connected)
                {
                    try
                    {
                        TCP.Close();
                        streamReader.Dispose();
                        streamWriter.Dispose();
                    }
                    catch (Exception) { } // Eat exceptions since this is just an attempt to clean up
                }

            }
            lock (timeoutTimer)
            {
                timeoutTimer.Dispose();
                timeoutTimer = new System.Timers.Timer(Timeout.TotalMilliseconds);
                timeoutTimer.Elapsed += TimeoutTimerElapsedHandler;
                timeoutTimer.Start();
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

            lock (registrationLock)
            {
                Disconnect();
                // Establish connection           
                Nick = nick; Username = user; RealName = realname; Host = host; Port = port; Password = password;
                TCP = new TcpClient();
            }
            Task connectTask = TCP.ConnectAsync(host, port);
            await connectTask;

            if (connectTask.Exception != null)
            {
                Exception = connectTask.Exception;
                if (OnException != null)
                    OnException(this, connectTask.Exception);
                throw connectTask.Exception; // If connect failed
            }

            Connected = true;

            // If connect succeeded

            streamReader = new System.IO.StreamReader(TCP.GetStream(), Encoding);
            streamWriter = new System.IO.StreamWriter(TCP.GetStream(), Encoding);

            // Register handler to on connect event
            OnRawMessageReceived += RegisterHandler;
            var readTask = streamReader.ReadLineAsync().ContinueWith(OnAsyncRead);
        }

        /// <summary>
        /// Sends an EOL-terminated message to the server (\n is appended by this method)
        /// </summary>
        /// <param name="format">Format of message to send</param>
        /// <param name="formatParameters">Format parameters</param>
        /// <returns></returns>
        public async Task<bool> SendRawMessage(String format, params String[] formatParameters)
        {

            String message = String.Format(format, formatParameters);
            try
            {
                Task t = streamWriter.WriteLineAsync(message);
                await t;
                if (t.Exception != null) throw t.Exception;
                t = streamWriter.FlushAsync();
                await t;
                if (t.Exception != null) throw t.Exception;
                if (OnRawMessageSent != null)
                    OnRawMessageSent(this, message);
                return true;
            }
            catch (Exception e)
            {
                Exception = e;
                if (OnException != null)
                    OnException(this, e);
                return false;
            }




        }
        /// <summary>
        /// Callback used by StreamReader when a read finishes
        /// </summary>
        /// <param name="task"></param>
        private void OnAsyncRead(Task<String> task)
        {

            try
            {

                if (task.Exception == null && task.Result != null)
                {

                    streamReader.ReadLineAsync().ContinueWith(OnAsyncRead);
                }
                else if (task.Result == null)
                    throw new System.IO.EndOfStreamException();
                else
                    throw task.Exception;

                if (OnRawMessageReceived != null)
                {
                    OnRawMessageReceived(this, task.Result);
                }

            }
            catch (Exception e)
            {
                Exception = e;
                if (OnException != null)
                    OnException(this, e);

            }
            finally
            {

            }



        }
        #endregion IO


        #region Internal Handlers
        private void TimeoutTimerElapsedHandler(Object sender, System.Timers.ElapsedEventArgs e)
        {
            lock (timeoutTimer)
            {
                if ((e.SignalTime - LastMessage) > Timeout)
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

        private void RegisterHandler(Object sender, String message)
        {
            if (!message.Contains("NOTICE AUTH"))
                return;
            lock (registrationLock)
            {
                if (Registered) return;
                Registered = true;

            }

            OnRawMessageReceived -= RegisterHandler;
            System.Threading.Thread.Sleep(1000);
            if (Password != null)
                SendRawMessage("PASS {0}", Password).Wait();
            SendRawMessage("NICK {0}", Nick).Wait();
            SendRawMessage("USER {0} 0 * :{1}", Username, RealName).Wait();





        }

        private void NamesReplyHandler(IrcClient sender, String channel, String names)
        {


            Channel channelObject = null;
            lock (channels)
            {
                channels.TryGetValue(channel.ToLower(), out channelObject);
                if (channelObject == null)
                    channelObject = new Channel(channel);

                String[] namesArray = names.Split(' ');
                foreach (String name in namesArray)
                {
                    int nameStart = 0;
                    for (nameStart = 0; sender.ServerInfo.PREFIX_symbols.Contains(name[nameStart]); ++nameStart) ;

                    String justName = name.Substring(nameStart);

                    ChannelUser user = null;

                    channelObject.Users.TryGetValue(ChannelUser.GetNickFromFullAddress(justName), out user);
                    if (user == null)
                    {
                        user = new ChannelUser(justName, channelObject);
                        channelObject.Users[user.Nick.ToLower()] = user;
                        /// If we are in the list, we need to be smart enough to add the channel to our list if it isnt there
                        if (user.Nick.Equals(Nick, StringComparison.CurrentCultureIgnoreCase))
                            channels[channelObject.Name.ToLower()] = channelObject;
                    }

                    for (int i = 0; i < nameStart; ++i)
                        user.InsertPrefix(sender.ServerInfo, channel, name[i]);
                }
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
            // :underworld2.no.quakenet.org 372 BenIsTesting :-
            var words = message.Split(' ');
            if (words.Length >= 3)
            {
                int numeric;
                if (int.TryParse(words[1], out numeric))
                {
                    if (OnRfcNumeric != null)
                    {
                        OnRfcNumeric(this, words[0], numeric, words[2], words.Length > 3 ? message.Substring(message.IndexOf(words[2]) + words[2].Length + 1) : null);
                    }
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

        public delegate void RfcPrivmsgHandler(IrcClient sender, String source, String target, String message);
        /// <summary>
        /// Called when a PRIVMSG is received from the server
        /// </summary>
        public event RfcPrivmsgHandler OnRfcPrivmsg;

        public delegate void RfcNamesReplyHandler(IrcClient sender, String channel, String list);
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
            channels.TryGetValue(name.ToLower(), out c);
            return c;
        }

    }

    public class Channel
    {
        public String Name { get; private set; }
        /// <summary>
        /// A map of lower(nick) -> user, for every user in the channel
        /// </summary>
        public IDictionary<String, ChannelUser> Users { get { return users; } }

        private IDictionary<String, ChannelUser> users = new ConcurrentDictionary<String, ChannelUser>();



        public Channel(String name)
        {

            Name = name;
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }
        public override bool Equals(object obj)
        {
            return obj is Channel && ((Channel)obj).Name.Equals(Name, StringComparison.CurrentCultureIgnoreCase);
        }
    }
    /// <summary>
    /// Represents another client on the IRC Server and in a channel
    /// </summary>
    public class ChannelUser
    {

        public String Nick { get; set; }
        public String Username { get; set; }
        public String Host { get; set; }

        public Channel Channel { get; private set; }
        public String FullAddress { get { return Nick == null || Username == null || Host == null ? null : String.Format("{0}!{1}@{2}", Nick, Username, Host); } }

        public String Prefixes
        {
            get { return prefixes.ToString(); }
        }

        /// <summary>
        /// The prefixes this user has on this channel
        /// </summary>
        private StringBuilder prefixes = new StringBuilder("");

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
        /// <param name="fulladdress"></param>
        /// <returns></returns>
        public static String GetNickFromFullAddress(String fulladdress)
        {
            if (!fulladdress.Contains('!'))
                return fulladdress;
            return fulladdress.Substring(0, fulladdress.IndexOf('!'));
        }
        /// <summary>
        /// Gets the user portion of a fulladdress, such as nick!USER@host
        /// </summary>
        /// <param name="fulladdress"></param>
        /// <returns></returns>
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
        /// <param name="fulladdress"></param>
        /// <returns></returns>
        public static String GetHostFromFullAddress(String fulladdress)
        {
            if (!fulladdress.Contains('@') || fulladdress.IndexOf('@') == fulladdress.Length)
                return null;
            return fulladdress.Substring(fulladdress.IndexOf('@') + 1);
        }



        /// <summary>
        /// Inserts a prefix (mode symbol) into this client's prefix list for the given channel and svrInfo class (must have PREFIX_symbols set by server)
        /// </summary>
        /// <param name="svrInfo"></param>
        /// <param name="channelName"></param>
        /// <param name="prefix"></param>
        internal void InsertPrefix(IrcClient.ServerInfoType svrInfo, String channelName, char prefix)
        {
            String currentList;
            lock (prefixes)
            {

                if (prefixes.ToString().Contains(prefix))
                    return;
                else if (prefixes.Length == 0)
                    prefixes.Append(prefix);
                else
                {
                    if (svrInfo.PREFIX_symbols == null) throw new Exception("Internal IRCClient error: PREFIX_symbols is NULL");

                    if (!svrInfo.PREFIX_symbols.Contains(prefix)) throw new Exception("Internal IRCClient error: PREFIX_symbols does not contain prefix which was inserted: " + prefix);

                    /// Find the first prefix in the current list (newList) whose value is less than this new prefix, and insert at that position
                    /// Or append it to the end if we never find one
                    for (int i = 0; i < prefixes.Length; ++i)
                    {
                        if (svrInfo.PREFIX_symbols.IndexOf(prefix) < svrInfo.PREFIX_symbols.IndexOf(prefixes[i]))
                        {
                            prefixes.Insert(i, prefix);
                            break;
                        }
                        else if (i + 1 == prefixes.Length) // If we've reached the end and still haven't found one of lower value, then this one belongs at the end
                        {
                            prefixes.Append(prefix);
                            return;
                        }
                    }
                }

            }
        }
        /// <summary>
        /// Deletes a prefix (mode symbol) from this client's prefix list for the given channel and svrInfo clas
        /// </summary>
        /// <param name="channelName"></param>
        /// <param name="prefix"></param>
        internal void DeletePrefix(String channelName, char prefix)
        {
            channelName = channelName.ToLower();
            lock (prefixes)
            {
                int prefixPosition = prefixes.ToString().IndexOf(prefix);
                if (prefixPosition > 0)
                {
                    prefixes.Remove(prefixPosition, 1);
                }

            }
        }
    }

}
