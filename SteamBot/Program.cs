using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CSGOTM;
using NDesk.Options;
using Server;
using SteamBot.Utility.MongoApi;
using Utility;

namespace SteamBot
{
    public class Program
    {
        private static OptionSet opts = new OptionSet()
                                     {
                                         {"bot=", "launch a configured bot given that bots index in the configuration array.", 
                                             b => botIndex = Convert.ToInt32(b) } ,
                                             { "help", "shows this help text", p => showHelp = (p != null) }
                                     };

        private static bool showHelp;

        private static int botIndex = -1;
        private static BotManager manager;
        private static bool isclosing = false;
        private static EventWaitHandle waitHandle = new AutoResetEvent(false);

        [STAThread]
        public static void Main(string[] args)
        {
#if CODEFORCES
            int done = 0;
            for (int i = 0; i < 20; ++i) {
                Task.Run(() => {
                    try {
                        FakeFactory.CreateFake();
                    } finally {
                        ++done;
                    }
                });
            }
            while (done < 20) {
                Thread.Sleep(10000);
            }
            return;
#endif
#if CORE
            Common.Utility.Environment.InitializeScope(true);
            int port = 4345;
            if (args.Length > 0) {
                port = int.Parse(args[0]);
            }
            NewCore core = new NewCore(port);
            core.Initialize();
            while (true) {
                string input = Console.ReadLine();
                if (input == "refresh") {
                    core.Initialize();
                }
                if (input == "exit")
                    return;
            }
#else
            Common.Utility.Environment.InitializeScope(false);
            opts.Parse(args);

            if (showHelp)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("If no options are given SteamBot defaults to Bot Manager mode.");
                opts.WriteOptionDescriptions(Console.Out);
                Console.Write("Press Enter to exit...");
                Console.ReadLine();
                return;
            }

            if (args.Length == 0)
            {
                BotManagerMode();
            }
            else if (botIndex > -1)
            {
                BotMode(botIndex);
            }
#endif
        }

#region SteamBot Operational Modes

        // This mode is to run a single Bot until it's terminated.
        private static void BotMode(int botIndex)
        {
            if (!File.Exists("settings.json"))
            {
                Console.WriteLine("No settings.json file found.");
                return;
            }

            Configuration configObject;
            try
            {
                configObject = Configuration.LoadConfiguration("settings.json");
            }
            catch (Newtonsoft.Json.JsonReaderException)
            {
                // handle basic json formatting screwups
                Console.WriteLine("settings.json file is corrupt or improperly formatted.");
                return;
            }

            if (botIndex >= configObject.Bots.Length)
            {
                Console.WriteLine("Invalid bot index.");
                return;
            }

            Bot b = new Bot(configObject.Bots[botIndex], configObject.ApiKey, BotManager.UserHandlerCreator, true, true);
            Console.Title = "Bot Manager";
            b.StartBot();

            string AuthSet = "auth";
            string ExecCommand = "exec";
            string InputCommand = "input";

            // this loop is needed to keep the botmode console alive.
            // instead of just sleeping, this loop will handle console input
            while (true)
            {
                string inputText = Console.ReadLine();

                if (String.IsNullOrEmpty(inputText))
                    continue;

                // Small parse for console input
                var c = inputText.Trim();

                var cs = c.Split(' ');

                if (cs.Length > 1)
                {
                    if (cs[0].Equals(AuthSet, StringComparison.CurrentCultureIgnoreCase))
                    {
                        b.AuthCode = cs[1].Trim();
                    }
                    else if (cs[0].Equals(ExecCommand, StringComparison.CurrentCultureIgnoreCase))
                    {
                        b.HandleBotCommand(c.Remove(0, cs[0].Length + 1));
                    }
                    else if (cs[0].Equals(InputCommand, StringComparison.CurrentCultureIgnoreCase))
                    {
                        b.HandleInput(c.Remove(0, cs[0].Length + 1));
                    }
                }
            }
        }

        // This mode is to manage child bot processes and take use command line inputs
        private static void BotManagerMode()
        {
            Console.Title = "Bot Manager";

            manager = new BotManager();
            bool loadedOk = false;
            bool primetime = LocalRequest.IsPrimeTime();
            if (!primetime) {
                return;
            }
            if (!File.Exists("settings.json")) {
                try {
                    loadedOk = manager.LoadConfigurationFromData(LocalRequest.GetConfig());
                } catch (Exception e) {
                    Console.WriteLine(
                        "Config file is missing and all instances are working.");
                    Console.Write("Press Enter to exit...");
                    Console.ReadLine();
                    return;
                }
            } else {
                loadedOk = manager.LoadConfiguration("settings.json");
            }

            if (!loadedOk)
            {
                Console.WriteLine(
                    "Configuration file Does not exist or is corrupt. Please rename 'settings-template.json' to 'settings.json' and modify the settings to match your environment");
                Console.Write("Press Enter to exit...");
                Console.ReadLine();
                return;
            }
            else
            {
                if (manager.ConfigObject.UseSeparateProcesses)
                    SetConsoleCtrlHandler(ConsoleCtrlCheck, true);
                Consts.Endpoints.juggler = manager.ConfigObject.JugglerEndpoint ?? "http://steambot.noobgam.me";
                Tasking.Run(manager.Nanny);

                if (manager.ConfigObject.AutoStartAllBots)
                {
                    var startedOk = manager.StartBots();

                    if (!startedOk)
                    {
                        Console.WriteLine(
                            "Error starting the bots because either the configuration was bad or because the log file was not opened.");
                        Console.Write("Press Enter to exit...");
                        Console.ReadLine();
                    }
                }
                else
                {
                    foreach (var botInfo in manager.ConfigObject.Bots)
                    {
                        if (botInfo.AutoStart)
                        {
                            // auto start this particual bot...
                            manager.StartBot(botInfo.Username);
                        }
                    }
                }

                Console.WriteLine("Type help for bot manager commands. ");

                var bmi = new BotManagerInterpreter(manager);

                // command interpreter loop.
                do
                {
                    Console.Write("botmgr > ");
                    string inputText = Console.ReadLine();
                    if (inputText == null) {
                        waitHandle.WaitOne();
                        return;
                    }

                    if (!String.IsNullOrEmpty(inputText))
                        bmi.CommandInterpreter(inputText);


                } while (!isclosing);
            }
        }

#endregion Bot Modes

        private static bool ConsoleCtrlCheck(CtrlTypes ctrlType)
        {
            // Put your own handler here
            switch (ctrlType)
            {
                case CtrlTypes.CTRL_C_EVENT:
                case CtrlTypes.CTRL_BREAK_EVENT:
                case CtrlTypes.CTRL_CLOSE_EVENT:
                case CtrlTypes.CTRL_LOGOFF_EVENT:
                case CtrlTypes.CTRL_SHUTDOWN_EVENT:
                    if (manager != null)
                    {
                        manager.StopBots();
                    }
                    isclosing = true;
                    waitHandle.Set();
                    break;
            }
            
            return true;
        }

#region Console Control Handler Imports

        // Declare the SetConsoleCtrlHandler function
        // as external and receiving a delegate.
        [DllImport("Kernel32")]
        public static extern bool SetConsoleCtrlHandler(HandlerRoutine Handler, bool Add);

        // A delegate type to be used as the handler routine
        // for SetConsoleCtrlHandler.
        public delegate bool HandlerRoutine(CtrlTypes CtrlType);

        // An enumerated type for the control messages
        // sent to the handler routine.
        public enum CtrlTypes
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT,
            CTRL_CLOSE_EVENT,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT
        }

#endregion
    }
}
