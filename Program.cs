using System;
using System.Diagnostics;
using System.IO;
using System.Data.SqlClient;
using System.Collections.Generic;
using System.Reflection;
using System.Collections;
using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;
using System.Threading;
using System.Configuration;
using System.Xml;


namespace SVC.MessageReceiver
{
    class ConsoleRuntime
    {

        public const string ServiceName = "SVC_MessageReceiver";
        public const string ServiceDisplayName = "SVC Message Receiver";
        public const string ServiceDescription = "A Service built Message Receiver to subscribe the Message in background.";
        public const string ProcessName = "SVC_MessageReceiver";
        private static object _isService;
        private static bool _rethrowException;
        private static SVCLogger logger = new SVCLogger();
        static int Main(string[] args)
        {
            bool install = false,
                 uninstall = false,
                 console = false;


            Console.WriteLine(@"SVC Message Receiver starts with following parameters: {0}", string.Join(", ", args));
            foreach (var arg in args)
            {
                switch (arg)
                {
                    case "-i":
                    case "-install":
                        install = true;
                        break;
                    case "-u":
                    case "-uninstall":
                        uninstall = true;
                        break;
                    case "-c":
                    case "-console":
                        console = true;
                        break;
                    default:
                        Console.Error.WriteLine("Argument not expected: " + arg);
                        // console = true;
                        break;
                }
            }

            try
            {
                if (uninstall)
                {
                    Console.WriteLine(@"SVC Message Receiver service will be uninstalled");
                    Install(true, args);
                }
                if (install)
                {
                    Console.WriteLine(@"SVC Message Receiver service will be installed");
                    Install(false, args);
                }

                if (console)
                {
                    Console.WriteLine(@"SVC Message Receiver executes with console mode");
                    RunConsole();
                }
                else if(!uninstall && !install)
                {
                    Console.WriteLine(@"SVC Message Receiver executes with console mode");
                    RunConsole();
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                logger.DebugLoger("Error during SVC Message Receiver Engine initialization" + ex.Message);

                if (_rethrowException) throw;
                Console.WriteLine(string.Concat("Error during SVC Message Receiver Engine initialization: ", ex.Message));
                return -1;
            }

        }
        /*
         Run the MQTT Message Receiver with Console mode
         */
        static void RunConsole()
        {
            var rc = new SVCMQTTMessageReceiverRunTime();
            rc.Process();
            Console.WriteLine(@"Press ENTER to shutdown.");
            Console.ReadLine();
            rc.Shutdown();
            Console.WriteLine(@"System stopped");
        }

        private static void Install(bool undo, string[] args)
        {
            try
            {
                logger.DebugLoger(string.Concat("{0} SVC Message Receiver Service", undo ? "Uninstalling" : "Installing"));
                Console.WriteLine(string.Concat("{0} SVC Message Receiver Service", undo ? "Uninstalling" : "Installing"));
                using (var inst = new
                    AssemblyInstaller(typeof(ConsoleRuntime).Assembly, args))
                {
                    IDictionary state = new Hashtable();
                    inst.UseNewContext = true;
                    try
                    {
                        if (undo)
                        {
                            StopService();
                            inst.Uninstall(state);
                        }
                        else
                        {
                            inst.Install(state);
                            inst.Commit(state);
                            //StartService();
                        }
                    }
                    catch
                    {
                        try
                        {
                            inst.Rollback(state);
                        }
                        // ReSharper disable EmptyGeneralCatchClause
                        catch
                        // ReSharper restore EmptyGeneralCatchClause
                        {
                        }
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
            }
        }

        private static void StopService()
        {
            if (!IsInstalled()) return;
            using (var controller = new ServiceController(ServiceName))
            {
                try
                {
                    if (controller.Status != ServiceControllerStatus.Stopped)
                    {
                        controller.Stop();
                        controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    }
                }
                // ReSharper disable EmptyGeneralCatchClause
                catch
                // ReSharper restore EmptyGeneralCatchClause
                {
                }
            }
        }

        private static bool IsInstalled()
        {
            using (var controller = new ServiceController(ServiceName))
            {
                try
                {
#pragma warning disable 168
                    ServiceControllerStatus status = controller.Status;
#pragma warning restore 168
                }
                catch
                {
                    return false;
                }
                return true;
            }
        }

        public static bool IsService
        {
            get
            {
                // Determining whether or not the host application is a service is
                // an expensive operation (it uses reflection), so we cache the
                // result of the first call to this method so that we don't have to
                // recalculate it every call.

                // If we have not already determined whether or not the application
                // is running as a service...

                if (_isService == null)
                {
                    _isService = Environment.CurrentDirectory == Environment.SystemDirectory;
                }
                // Return the cached result.
                return Convert.ToBoolean(_isService);
            }
        }
    }


    #region Nested type: MyServiceInstaller

    [RunInstaller(true)]
    public sealed class MyServiceInstaller : ServiceInstaller
    {
        public MyServiceInstaller()
        {
            this.Description = ConsoleRuntime.ServiceDescription;
            this.DisplayName = ConsoleRuntime.ServiceDisplayName;
            this.ServiceName = ConsoleRuntime.ServiceName;
            this.StartType =
                ServiceStartMode.Manual;
        }
    }

    #endregion

    #region Nested type: MyServiceInstallerProcess

    [RunInstaller(true)]
    public sealed class MyServiceInstallerProcess :
        ServiceProcessInstaller
    {
        public MyServiceInstallerProcess()
        {
            this.Account = ServiceAccount.LocalSystem;
        }
    }

    #endregion

    public class WinService : ServiceBase
    {
        private SVCMQTTMessageReceiverRunTime _runtime = new SVCMQTTMessageReceiverRunTime();

        private Thread _InitializeThread;
        
        private DateTime _lastRunTime;
        private static SVCLogger logger = new SVCLogger();
        protected override void OnStart(string[] args)
        {
            this._InitializeThread = new Thread(this.Start);

            this._lastRunTime = DateTime.MinValue;

            this._InitializeThread.Start();


        }

        private void Start()
        {
            try
            {
                _runtime.Process();
            }
            catch (ThreadAbortException)
            {
                logger.InfoLoger("SVC Message receiver initialization aborted");
            }
            catch (Exception e)
            {
                logger.InfoLoger("Error during SVC Message Receiver initialization :"+ e.Message);
                Environment.Exit(-1);
            }
        }


        /// <summary>
        /// Stop this service.
        /// </summary>
        protected override void OnStop()
        {
            if (this._InitializeThread != null)
            {
                try
                {
                    if (this._InitializeThread.ThreadState != System.Threading.ThreadState.Stopped &&
                        this._InitializeThread.ThreadState != System.Threading.ThreadState.Unstarted)
                        this._InitializeThread.Abort();
                }
                catch (ThreadStateException)
                {
                    //do not throw exceptions when Abort failed because of state
                }
                catch (Exception e)
                {
                   logger.InfoLoger("Error during SVC Message Receiver stop" + e.Message);
                }
                finally
                {
                    this._InitializeThread.Join();
                    this._InitializeThread = null;
                }
            }

            logger.InfoLoger("Shutting down SVC Message Receiver.");
        }

    }

    public class SVCMQTTMessageReceiverRunTime
    {
        private SVCMQTTMessageReceiver runtimeexecutor; 
        DateTime[] _lastruntimelist;
        private static bool running = true;
        private SVCLogger logger = new SVCLogger();
       
        public void Process()
        {
            try
            {
                logger.DebugLoger(typeof(WinService) + " Start Process");

                this.ReadMqttConfigurationxml();

            }
            catch (ThreadAbortException)
            {
                logger.InfoLoger("SVC Message Receiver initialization aborted");
            }
            catch (Exception e)
            {
                logger.InfoLoger("Error during SVC Message Receiver initialization :"+ e.Message);
                //       Environment.Exit(-1);
            }
        }
        private void ReadMqttConfigurationxml()
        {
            string MqttbrokerAlias;
            string Mqttbroker;
            int MqttbrokerPort = 0;
            string UserName;
            string Password;
            string Certificationfile;
            string RootCertfile;
            string sslprotocol;
            string ConnectionMethod;
            string[] MqttTopicList;
            string[] MqttTopicAliasList;
            string RestWebServiceUrl;
            string baseCallRestWebServiceUrl;
            string RestApiKey;
            string RestClientID;

            string commRestWebServiceUrl = "";
            string commbaseCallRestWebServiceUrl = "";
            string commRestApiKey = "";
            string commRestClientID = "";

            XmlDocument doc = new XmlDocument();
            XmlDocument targetdoc = new XmlDocument();
            XmlDocument instancedoc = new XmlDocument();
            XmlDocument brokedoc = new XmlDocument();
            XmlDocument topicdoc = new XmlDocument();
            XmlNodeList nodes;

            string instance = SVCCommon.AppConfigValue("instance");

            if (instance == "")
            {
                logger.InfoLoger("There is no instance value!");
                throw new Exception("There is no instance value");
            }

            string mqttconfig = SVCCommon.AppConfigValue("mqttconfiguration");

            if (mqttconfig == "")
            {
                logger.InfoLoger("There is no mqtt configuration file value! bypass mqtt receiver!");
                return;
            }

            string filename = mqttconfig;

            if (mqttconfig.StartsWith("\\") || mqttconfig.StartsWith(".."))
                filename = SVCCommon.GetSolutionPath() + mqttconfig;

            doc.Load(filename);

            XmlNodeList instancenodes = doc.GetElementsByTagName("instance");

            bool findinstance = false;

            foreach (XmlNode node in instancenodes)
            {
                //node.Attributes.GetNamedItem("name");
              //  node.Attributes.GetNamedItem("name").Value;
                if (node.Attributes.GetNamedItem("name").Value == instance)
                {
                    instancedoc.LoadXml("<root>" + node.InnerXml + "</root>");
                    findinstance = true;
                }
            }

            if (!findinstance)
            {
                logger.DebugLoger("There is no mqtt configuration node for instance:" + instance);
                return;
            }

            XmlNodeList targetnodes = doc.GetElementsByTagName("target");
            if (targetnodes.Count > 0) {
                targetdoc.LoadXml("<root>" + targetnodes[0].InnerXml + "</root>");

                nodes = targetdoc.GetElementsByTagName("basecallrestwebserviceurl");
                if (nodes.Count > 0)
                    commbaseCallRestWebServiceUrl = nodes[0].InnerText.Trim();

                nodes = targetdoc.GetElementsByTagName("restwebserviceurl");
                if (nodes.Count > 0)
                    commRestWebServiceUrl = nodes[0].InnerText.Trim();

                nodes = targetdoc.GetElementsByTagName("restapikey");
                if (nodes.Count > 0)
                    commRestApiKey = nodes[0].InnerText.Trim();

                nodes = targetdoc.GetElementsByTagName("clientid");
                if (nodes.Count > 0)
                    commRestClientID = nodes[0].InnerText.Trim();
            }

            XmlNodeList rnodes = instancedoc.GetElementsByTagName("mqtt");

            foreach (XmlNode rnode in rnodes)
            {
                MqttbrokerAlias = "";
                Mqttbroker = "";
                MqttbrokerPort = 0;
                UserName = "";
                Password = "";
                Certificationfile = "";
                RootCertfile = "";
                sslprotocol = "";
                ConnectionMethod = "";
                MqttTopicList = new string[] { };
                MqttTopicAliasList = new string[] { };
                RestWebServiceUrl = "";
                baseCallRestWebServiceUrl = "";
                RestApiKey = "";
                RestClientID = "";

                logger.DebugLoger(rnode.InnerXml);
                brokedoc.LoadXml("<mqtt>"+rnode.InnerXml+"</mqtt>");

                nodes = brokedoc.GetElementsByTagName("broker");
                
                if (nodes.Count > 0)
                    Mqttbroker = nodes[0].InnerText.Trim();

                if(Mqttbroker == "")
                {
                    logger.DebugLoger("There is no broker configured!");
                    return;
                }

                nodes = brokedoc.GetElementsByTagName("alias");

                if (nodes.Count > 0)
                    MqttbrokerAlias = nodes[0].InnerText.Trim();

                if (MqttbrokerAlias == "")
                    MqttbrokerAlias = Mqttbroker;

                nodes = brokedoc.GetElementsByTagName("brokerport");
                if (nodes.Count > 0)
                    if(nodes[0].InnerText.Trim() !="")
                        MqttbrokerPort = int.Parse(nodes[0].InnerText.Trim());

                nodes = brokedoc.GetElementsByTagName("username");
                if (nodes.Count > 0)
                    UserName = nodes[0].InnerText.Trim();

                nodes = brokedoc.GetElementsByTagName("password");
                if (nodes.Count > 0)
                    Password = nodes[0].InnerText.Trim();

                nodes = brokedoc.GetElementsByTagName("certificationfile");
                if (nodes.Count > 0)
                    Certificationfile = nodes[0].InnerText.Trim();

                nodes = brokedoc.GetElementsByTagName("rootcertfile");
                if (nodes.Count > 0)
                    RootCertfile = nodes[0].InnerText.Trim();

                nodes = brokedoc.GetElementsByTagName("sslprotocol");
                if (nodes.Count > 0)
                {
                    sslprotocol = nodes[0].InnerText.Trim().ToUpper();
 
                }                 

                nodes = brokedoc.GetElementsByTagName("basecallrestwebserviceurl");
                if (nodes.Count > 0)
                    baseCallRestWebServiceUrl = nodes[0].InnerText.Trim();

                nodes = brokedoc.GetElementsByTagName("restwebserviceurl");
                if (nodes.Count > 0)
                    RestWebServiceUrl = nodes[0].InnerText.Trim();

                nodes = brokedoc.GetElementsByTagName("restapikey");
                if (nodes.Count > 0)
                    RestApiKey = nodes[0].InnerText.Trim();

                nodes = brokedoc.GetElementsByTagName("clientid");
                if (nodes.Count > 0)
                    RestClientID = nodes[0].InnerText.Trim();

                nodes = brokedoc.GetElementsByTagName("topics");
                topicdoc.LoadXml("<topics>"+nodes[0].InnerXml+"</topics>");
                XmlNodeList topicnodes = topicdoc.GetElementsByTagName("topic");
                MqttTopicList = new string[topicnodes.Count];
                MqttTopicAliasList = new string[topicnodes.Count];
                var i = 0;
                foreach (XmlNode topicnode in topicnodes)
                {
                    if (topicnode.InnerText.Trim() != "")
                    {
                        string alias = topicnode.Attributes.GetNamedItem("alias").Value;

                        MqttTopicList[i] = topicnode.InnerText.Trim();

                        if (alias == "")
                        {
                            MqttTopicAliasList[i] = topicnode.InnerText.Trim();
                        }
                        else
                            MqttTopicAliasList[i] = alias;

                        i += 1;
                    }
                }

                if (Mqttbroker != "" && MqttTopicList.Length > 0)
                {
                    runtimeexecutor = new SVCMQTTMessageReceiver();
                    runtimeexecutor.Mqttbroker = Mqttbroker;
                    runtimeexecutor.MqttbrokerAlias = MqttbrokerAlias;
                    runtimeexecutor.MqttbrokerPort = MqttbrokerPort;
                    runtimeexecutor.UserName = UserName;
                    runtimeexecutor.Password = Password;
                    runtimeexecutor.Certificationfile = Certificationfile;
                    runtimeexecutor.RestApiKey = RestApiKey;
                    runtimeexecutor.RootCertfile = RootCertfile;
                    runtimeexecutor.RestWebServiceUrl = RestWebServiceUrl;
                    runtimeexecutor.ssl = sslprotocol;
                    runtimeexecutor.baseCallRestWebServiceUrl = baseCallRestWebServiceUrl;
                    runtimeexecutor.MqttTopicList = MqttTopicList;
                    runtimeexecutor.MqttTopicAliasList = MqttTopicAliasList;
                    runtimeexecutor.RestClientID = RestClientID;
                    runtimeexecutor.commRestWebServiceUrl = commRestWebServiceUrl;
                    runtimeexecutor.commbaseCallRestWebServiceUrl = commbaseCallRestWebServiceUrl;
                    runtimeexecutor.commRestApiKey = commRestApiKey;
                    runtimeexecutor.commRestClientID = commRestClientID;
                    runtimeexecutor.SVCMQTTConnect();
                }
 
            }
        }
        public void Shutdown()
        {
            runtimeexecutor.Disconnect = true;
            running = false;
        }

    }

 
}
