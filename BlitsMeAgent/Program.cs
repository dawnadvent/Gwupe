﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace Gwupe.Agent
{
    static class Program
    {
#if DEBUG
        public const String BuildMarker = "_Dev";
#else
        public const String BuildMarker = "";
#endif
        private const string AppGuid = "Gwupe.Agent" + BuildMarker + ".Application";

        // unmanaged code doesn't like to be loaded via byte arrays, only via files, so we need to write them out and load them back in
        static private readonly String[] UnmanagedAssemblies = { "UdtProtocol" };

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(String[] args)
        {
            // never run as system user
            if (Environment.UserName.Equals("SYSTEM")) return;
            using (Mutex mutex = new Mutex(false, AppGuid))
            {
                if (!mutex.WaitOne(0, false))
                {
                    var process = Common.OsUtils.GetMyDoppleGangerProcess();
                    if (process != null)
                    {
                        var outcome = Common.OsUtils.PostMessage((IntPtr)Common.OsUtils.HWND_BROADCAST, Common.OsUtils.WM_SHOWGWUPE,
#if DEBUG
                            IntPtr.Zero,
#else
                            new IntPtr(1), 
#endif
                            IntPtr.Zero);
                    }
                    else
                    {
                        MessageBox.Show("Gwupe" + BuildMarker + " Is already running.");
                    }
                    return;
                }
                // Make sure we load certain namespaces as resources (they are embedded dll's)
                AppDomain.CurrentDomain.AssemblyResolve += EmbeddedAssemblyResolver;

                GC.Collect();
                try
                {
                    Thread.CurrentThread.Name = "MAIN";
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    var options = new List<GwupeOption>();
                    foreach (string argument in args)
                    {
                        if (argument.ToLower().Equals("/minimize"))
                        {
                            options.Add(GwupeOption.Minimize);
                        }
                    }
                    Application.ThreadException += Application_ThreadException;
                    AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
                    Application.Run(new GwupeClientAppContext(options));
                }

                catch
                    (Exception
                    ex)
                {

                    MessageBox.Show(ex.Message + (ex.InnerException != null ? " : " + ex.InnerException.Message : "") + " : " + ex.StackTrace,
                                    "Program Terminated Unexpectedly",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            LogFaultReportOnUnhandledExceptions(e.Exception);
        }

        private static void LogFaultReportOnUnhandledExceptions(Exception ex)
        {
            try
            {
                if (GwupeClientAppContext.CurrentAppContext != null)
                {
                    GwupeClientAppContext.CurrentAppContext.SubmitFaultReport(new FaultReport()
                    {
                        Subject = "Unhandled exception error",
                        UserReport = ex.ToString()
                    });
                }
            }
            catch
            {
                throw ex;
            }
        }

        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LogFaultReportOnUnhandledExceptions((Exception)e.ExceptionObject);
        }

        private static Assembly EmbeddedAssemblyResolver(object sender, ResolveEventArgs args)
        {
            try
            {
                var assemblyName = new AssemblyName(args.Name);
                String resourceName = Assembly.GetExecutingAssembly().FullName.Split(',').First() + "." + assemblyName.Name + ".dll";
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        Byte[] assemblyData = new Byte[stream.Length];
                        stream.Read(assemblyData, 0, assemblyData.Length);
                        if (Array.Exists(UnmanagedAssemblies, element => element.Equals(assemblyName.Name)))
                        {
                            String tempFile = Path.GetTempFileName();
                            File.WriteAllBytes(tempFile, assemblyData);
                            Console.WriteLine("[" + Thread.CurrentThread.ManagedThreadId + "-" + Thread.CurrentThread.Name + "] Loading assembly " + assemblyName.Name + " from " + tempFile);
                            return Assembly.LoadFile(tempFile);
                        }
                        else
                        {
                            return Assembly.Load(assemblyData);
                        }
                    }
                }
            }
            catch (Exception ex)
            {

                MessageBox.Show(ex.Message + (ex.InnerException != null ? " : " + ex.InnerException.Message : ""), "Program Failed to access Assembly " + args.Name,
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return null;
        }
    }

}
