using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Net.Sockets;

namespace benbuzbee.LRTIRC
{
    public class IrcClient
    {

        #region Properties
        public Exception Exception { private set; get; }
        public TcpClient TCP { private set; get; }
        public String Nick { private set; get; }
        public String User { private set; get; }
        public String RealName { private set; get; }
        public String Host { private set; get; }
        public int Port { private set; get; }
        public String Password { private set; get; }
        public DateTime LastMessage { private set; get; }
        public bool Connected { private set; get; }
        /// <summary>
        /// How long without a message before we time out
        /// </summary>
        public TimeSpan Timeout { set; get; }
        /// <summary>
        /// Sets the Encoding. Defaults to UTF8 without BOM. Must reconnect after changing.
        /// </summary>
        public Encoding Encoding { set; get; }
        public Boolean Registered { private set; get; }
        #endregion Properties

        #region Private Members
        private Object registrationLock = new Object();

        private System.IO.StreamReader streamReader;
        private System.IO.StreamWriter streamWriter;
        private System.Timers.Timer timeoutTimer = new System.Timers.Timer();
        #endregion



        public IrcClient()
        {
            Encoding = new System.Text.UTF8Encoding(false);
            Registered = false;
            LastMessage = DateTime.Now;
            Timeout = new TimeSpan(0, 5, 0);
            Connected = false;

            #region Register Delegates

            OnRawMessageReceived += ErrorHandler; // This one looks for ERROR messages
            OnRawMessageReceived += RegisterHandler;
            OnRawMessageReceived += PingHandler;
            OnRawMessageReceived += NumericHandler;
            OnRawMessageReceived += PrivmsgHandler;
            OnRfcNumeric += (sender, source, numeric, target, other) => { if (numeric == 1 && OnConnect != null) OnConnect(this); };
            OnRawMessageReceived += (source, message) => { LastMessage = DateTime.Now; };

            #endregion Register Delegates
        }

        #region IO
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
        public async Task Connect(String nick, String user, String realname, String host, int port = 6667, String password = null)
        {

            lock (registrationLock)
            { 
                Disconnect();
                // Establish connection           
                Nick = nick; User = user; RealName = realname; Host = host; Port = port; Password = password;
                TCP = new TcpClient();
            }
            Task connectTask = TCP.ConnectAsync(host, port);
            connectTask.Wait();

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

            streamReader.ReadLineAsync().ContinueWith(OnAsyncRead);

        }

        public async Task<bool> SendRawMessage(String messageFormat, params String[] messageData)
        {

            String message = String.Format(messageFormat, messageData);
            try
            {
                await streamWriter.WriteAsync(message + "\n");
                Task t = streamWriter.FlushAsync();
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
                SendRawMessage("PASS {0}", Password);
            SendRawMessage("NICK {0}", Nick);
            SendRawMessage("USER {0} 0 * :{1}", User, RealName);





        }

        private void PingHandler(Object sender, String message)
        {
            if (message.StartsWith("PING"))
                SendRawMessage("PONG {0}", message.Substring("PING ".Length));
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
        public event IrcExceptionHandler OnException;

        public delegate void IrcRawMessageHandler(IrcClient sender, String message);
        public event IrcRawMessageHandler OnRawMessageReceived;
        public event IrcRawMessageHandler OnRawMessageSent;

        public delegate void RfcOnErrorHandler(IrcClient sender, String message);
        public event RfcOnErrorHandler OnRfcError;

        public delegate void RfcNumericHandler(IrcClient sender, String source, int numeric, String target, String other);
        public event RfcNumericHandler OnRfcNumeric;

        public event Action<IrcClient> OnConnect;

        public delegate void RfcPrivmsgHandler(IrcClient sender, String source, String target, String message);
        public event RfcPrivmsgHandler OnRfcPrivmsg;
        #endregion Events and Delegates

        /// <summary>
        /// Called when LastMessage was more than Timeout ago
        /// </summary>
        public event Action<IrcClient> OnTimeout;
    }


}
