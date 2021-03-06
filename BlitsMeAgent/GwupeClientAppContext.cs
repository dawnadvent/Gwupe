﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Gwupe.Agent.Components;
using Gwupe.Agent.Components.Schedule;
using Gwupe.Agent.Exceptions;
using Gwupe.Agent.Managers;
using Gwupe.Agent.Misc;
using Gwupe.Cloud.Messaging.Request;
using Gwupe.Cloud.Messaging.Response;
using Gwupe.Common;
using Gwupe.Common.Security;
using Gwupe.ServiceProxy;
using log4net;
using log4net.Appender;
using log4net.Config;
using log4net.Core;
using log4net.Repository.Hierarchy;

namespace Gwupe.Agent
{
    public enum GwupeOption
    {
        Minimize
    };

    public class GwupeClientAppContext : ApplicationContext
    {
        internal List<GwupeOption> Options { get; private set; }
        private static readonly ILog Logger = LogManager.GetLogger(typeof(GwupeClientAppContext));
        internal readonly GwupeServiceProxy GwupeServiceProxy;
        //public Dashboard UIDashBoard;
        internal P2PManager P2PManager;
        private RequestManager _requestManager;
        internal CurrentUserManager CurrentUserManager { get; private set; }
        internal RosterManager RosterManager { get; private set; }
        internal LoginManager LoginManager { get; private set; }
        internal ConnectionManager ConnectionManager { get; private set; }
        internal EngagementManager EngagementManager { get; private set; }
        internal NotificationManager NotificationManager { get; private set; }
        internal SearchManager SearchManager { get; private set; }
        internal UIManager UIManager { get; private set; }
        internal RepeaterManager RepeaterManager { get; private set; }
        internal SettingsManager SettingsManager { get; private set; }
        internal TeamManager TeamManager { get; private set; }
        internal RelationshipManager RelationshipManager { get; private set; }
        internal PartyManager PartyManager { get; private set; }
        //internal Thread DashboardUiThread;
        internal bool IsShuttingDown { get; private set; }
        internal readonly GwupeUserRegistry Reg = GwupeUserRegistry.getInstance();
        internal readonly String StartupVersion;
        internal readonly ScheduleManager ScheduleManager;
        private IdleState _idleState;
        internal ObservableCollection<String> ChangeLog = new ObservableCollection<string>();
        internal string ChangeDescription { get; set; }
        private ElevateToken CurrentToken { get; set; }

        internal static GwupeClientAppContext CurrentAppContext;

        /// <summary>
        /// This class should be created and passed into Application.Run( ... )
        /// </summary>
        /// <param name="options"> </param>
        public GwupeClientAppContext(List<GwupeOption> options)
        {
            CurrentAppContext = this;
            Options = options;
            XmlConfigurator.Configure(Assembly.GetExecutingAssembly().GetManifestResourceStream("Gwupe.Agent.log4net.xml"));
            StartupVersion = Regex.Replace(FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion, "\\.[0-9]+$", "");
            Logger.Info("Gwupe" + Program.BuildMarker + ".Agent Starting up [" + StartupVersion + "]");
#if DEBUG
            foreach (var manifestResourceName in Assembly.GetExecutingAssembly().GetManifestResourceNames())
            {
                Logger.Debug("Embedded Resource : " + manifestResourceName);
            }
#endif
            GwupeServiceProxy = new GwupeServiceProxy();
            ConnectionManager = new ConnectionManager();
            LoginManager = new LoginManager();
            P2PManager = new P2PManager();
            RosterManager = new RosterManager();
            EngagementManager = new EngagementManager();
            NotificationManager = new NotificationManager();
            SearchManager = new SearchManager();
            CurrentUserManager = new CurrentUserManager();
            TeamManager = new TeamManager();
            SettingsManager = new SettingsManager();
            UIManager = new UIManager();
            _requestManager = new RequestManager();
            ScheduleManager = new ScheduleManager();
            ScheduleManager.AddTask(new CheckUpgradeTask(this) { PeriodSeconds = 120 });
            ScheduleManager.AddTask(new CheckServiceTask(this) { PeriodSeconds = 120 });
            ScheduleManager.AddTask(new DetectIdleTask(this));
            RepeaterManager = new RepeaterManager();
            RelationshipManager = new RelationshipManager();
            PartyManager = new PartyManager();
            SetupChangeLog();
            // Start all the Active Managers
            UIManager.Start();
            ScheduleManager.Start();
            ConnectionManager.Start();
            LoginManager.Start();
            // Set correct last version
            Reg.LastVersion = StartupVersion;
        }


        private void SetupChangeLog()
        {
            if (!StartupVersion.Equals(Reg.LastVersion))
            {
                Logger.Debug("Gwupe has been upgraded, reading and displaying changelog");
                try
                {
                    using (
                        Stream stream =
                            Assembly.GetExecutingAssembly().GetManifestResourceStream("Gwupe.Agent.changelog.txt"))
                    {
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            if (reader.Peek() >= 0)
                            {
                                ChangeDescription = reader.ReadLine();
                            }
                            while (reader.Peek() >= 0)
                            {
                                ChangeLog.Add(reader.ReadLine());
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error("Failed to read changelog file : " + e.Message);
                }
            }
        }


        internal bool Debug
        {
            set { ((Logger)Logger.Logger).Parent.Level = value ? Level.Debug : Level.Info; }
        }

        public String Version(int level = 1)
        {
            var fullVersion = new Version(FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion);
            return fullVersion.Major +
                   (level > 0
                        ? "." + fullVersion.Minor +
                          (level > 1
                               ? "." + fullVersion.Build +
                                 (level > 2 ? "." + fullVersion.Revision : "")
                               : "")
                        : "");
        }

        public bool RestartGwupeService()
        {
            Process restartProcess = null;
            try
            {
                restartProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        Verb = OsUtils.IsWinVistaOrHigher ? "runas" : "",
                        FileName = Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]) +
                                   "\\GwupeRestartService.exe"
                    }
                };
                restartProcess.Start();
                restartProcess.WaitForExit();

                if (restartProcess.ExitCode != 0)
                {
                    Logger.Error("Failed to restart GwupeService");
                    ThreadPool.QueueUserWorkItem(state =>
                        SubmitFaultReport(new FaultReport()
                        {
                            Subject = "Gwupe Service restart failure.",
                            UserReport = "Failed to restart GwupeService manually"
                        }));
                    return false;
                }
                else
                {
                    Logger.Info("Restarted Gwupe Service");
                    return true;
                }
            }
            catch (Exception e)
            {
                Logger.Error("Attempt to call Gwupe Service Restarter failed", e);
                ThreadPool.QueueUserWorkItem(state =>
                    SubmitFaultReport(new FaultReport()
                    {
                        Subject = "Gwupe Service restarter error",
                        UserReport = "Attempt to call Gwupe Service Restarter failed : " + e
                    }));
                return false;
            }
            finally
            {
                if (restartProcess != null)
                {
                    restartProcess.Close();
                }
            }
        }

        public GwupeServiceProxy GwupeService
        {
            get { return GwupeServiceProxy; }
        }

        public event EventHandler IdleChanged;

        public void OnIdleChanged(EventArgs e)
        {
            EventHandler handler = IdleChanged;
            if (handler != null) handler(this, e);
        }

        public IdleState IdleState
        {
            get { return _idleState; }
            set { _idleState = value; OnIdleChanged(EventArgs.Empty); }
        }

        public void GenerateFaultReport()
        {
            FaultReport report = UIManager.GenerateFaultReport();
            // Submit the report in the background
            if (report != null)
            {
                ThreadPool.QueueUserWorkItem(state => SubmitFaultReport(report));
            }
        }

        public void SubmitFaultReport(FaultReport report)
        {
            // get the log file data
            var rootAppender = ((Hierarchy)LogManager.GetRepository()).Root.Appenders.OfType<FileAppender>().FirstOrDefault();
            string filename = rootAppender != null ? rootAppender.File : string.Empty;
#if DEBUG
            string serviceFilename = @"C:\Windows\Temp\GwupeService_Dev.log";
#else
            string serviceFilename = @"C:\Windows\Temp\GwupeService.log";
#endif
            if (!String.IsNullOrEmpty(filename))
            {
                try
                {
                    var request = new FaultReportRq
                    {
                        report = report.UserReport,
                        subject = report.Subject,
                        version = Version(3),
                        platform = Environment.OSVersion.ToString()
                    };
                    try
                    {
                        request.log = Convert.ToBase64String(ExtractLog(filename));
                    }
                    catch (Exception e)
                    {
                        Logger.Error("Failed to extract log from " + filename, e);
                        request.report += "\n\n\nFailed to extract log from " + filename;
                    }
                    try
                    {
                        request.serviceLog = Convert.ToBase64String(ExtractLog(serviceFilename));
                    }
                    catch (Exception e)
                    {
                        Logger.Error("Failed to extract log from " + serviceFilename, e);
                        request.report += "\n\n\nFailed to extract log from " + filename;
                    }
                    try
                    {
                        var response = ConnectionManager.Connection.Request<FaultReportRq, FaultReportRs>(request);
                    }
                    catch (Exception e)
                    {
                        Logger.Error("Failed to send fault report to server", e);
                    }
                }
                catch (Exception e)
                {
                    Logger.Error("Failed to read the log file", e);
                }
            }
            else
            {
                Logger.Error("Failed to get the log file, cannot submit fault.");
            }
        }

        private static byte[] ExtractLog(string filename)
        {
            Logger.Debug("Retrieving log from " + filename);
            FileStream stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long toRead = stream.Length < 131072 ? stream.Length : 131072;
            stream.Seek(-toRead, SeekOrigin.End);
            byte[] buffer = new byte[toRead];
            var read = stream.Read(buffer, 0, (int)toRead);
            stream.Close();
            byte[] zippedLog = Common.Misc.Instance().Zip(buffer);
            return zippedLog;
        }

        internal ElevateToken Elevate()
        {
            return Elevate("The secure action you are requesting requires you to enter your current Gwupe password to verify your identity.");
        }

        internal ElevateToken Elevate(String message)
        {
            if (CurrentToken == null || CurrentToken.IsExpired())
            {
                Logger.Debug("We don't have an elevation token or it has expired, requesting from server.");
                ElevateTokenRq erq = new ElevateTokenRq();
                try
                {
                    String password = UIManager.RequestElevation(message);
                    // Make sure password isn't empty and conforms to the current password.
                    if (!String.IsNullOrWhiteSpace(password) &&
                        Util.getSingleton().hashPassword(password).Equals(Reg.PasswordHash))
                    {
                        ElevateTokenRs ers = ConnectionManager.Connection.Request<ElevateTokenRq, ElevateTokenRs>(erq);
                        CurrentToken = new ElevateToken(ers.tokenId, ers.token, password, ers.expires);
                    }
                    else
                    {
                        Logger.Error("Entered password for elevation is invalid.");
                        CurrentToken = null;
                        throw new ElevationException();
                    }
                }
                finally
                {
                    UIManager.CompleteElevation();
                }
            }
            else
            {
                Logger.Debug("Reusing elevate token " + CurrentToken.TokenId);
            }
            return CurrentToken;
        }

        // Handle messages from the dashboard window
        public IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == OsUtils.WM_SHOWGWUPE &&
#if DEBUG
                wParam.ToInt32() == 0
#else
                wParam.ToInt32() == 1
#endif
)
            {
                Logger.Debug("Received show message to my handle " + hwnd);
                UIManager.Show();
                handled = true;
            }
            else if (msg == OsUtils.WM_SHUTDOWNGWUPE &&
#if DEBUG
                wParam.ToInt32() == 0
#else
                wParam.ToInt32() == 1
#endif
)
            {
                Logger.Debug("Received shutdown message to my handle " + hwnd);
                Thread shutdownThread = new Thread(Shutdown) { IsBackground = true, Name = "shutdownByMessageThread" };
                shutdownThread.Start();
            }
            else if (msg == OsUtils.WM_UPGRADEGWUPE &&
#if DEBUG
                wParam.ToInt32() == 0
#else
                wParam.ToInt32() == 1
#endif
)
            {
                Logger.Debug("Received upgrade message to my handle " + hwnd);
                Thread upgradeThread = new Thread(new CheckUpgradeTask(this).RunTask) { IsBackground = true, Name = "upgradeByMessageThread" };
                upgradeThread.Start();
            }
            return IntPtr.Zero;
        }


        public void Shutdown()
        {
            ExitThread();
        }

        // On exit
        protected override void ExitThreadCore()
        {
            this.IsShuttingDown = true;
            // before we exit, lets cleanup
            // Stop scheduled things from happening
            if (ScheduleManager != null)
                ScheduleManager.Close();
            // Logout and prevent trying to log back in
            if (LoginManager != null)
                LoginManager.Close();
            // Close up the engagements
            if (EngagementManager != null)
                EngagementManager.Close();
            // No more notifications
            if (NotificationManager != null)
                NotificationManager.Close();
            // Clear and close contact list
            if (RosterManager != null)
                RosterManager.Close();
            // No more searching
            if (SearchManager != null)
                SearchManager.Close();
            // Don't need service contact anymore
            if (GwupeServiceProxy != null)
                GwupeServiceProxy.close();
            // Connection can close
            if (ConnectionManager != null)
                ConnectionManager.Close();
            // Stop showing stuff
            if (UIManager != null)
                UIManager.Close();
            // Done
            Logger.Info("Gwupe.Agent has shut down");
            base.ExitThreadCore();
        }
    }
}