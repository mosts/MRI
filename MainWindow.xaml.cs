using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using MRI.praesidia;
﻿using MRI.resources;
using Newtonsoft.Json.Linq;

namespace MRI
{
    public partial class MainWindow : Window
    {
        // --------------- //
        // ** VARIABLES ** //
        // --------------- //
        public readonly string RobloxPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox");
        public bool LastTaskIsRoblox = false; // 2nd log is the real one
        public FileInfo? Last;
        public Mutex? RobloxLock;
        public Mutex? OtherRobloxLock;
        public FileStream? RobloxCookieLock;
        public readonly string WindowsUser = Environment.UserDomainName + "\\" + Environment.UserName;

        // ----------------------- //
        // ** GENERAL FUNCTIONS ** //
        // ----------------------- //
        public int RobloxInstancesOpen()
        {
            Process[] pname = Process.GetProcessesByName("RobloxPlayerBeta");
            return pname.Length;
        }

        // to simplify
        public FileInfo? MostRecentRobloxLogFile()
        {
            DirectoryInfo Directory = new DirectoryInfo(System.IO.Path.Combine(RobloxPath, "logs"));
            FileInfo[] FileInf = Directory.GetFiles();

            // Find the most recently edited file
            FileInfo? MostRecent = FileInf.OrderByDescending(file => file.LastWriteTime).FirstOrDefault();

            if (MostRecent != null)
            {
                Console.WriteLine("Most recently edited file:");
                Console.WriteLine($"Name: {MostRecent.Name}");
                Console.WriteLine($"Last Write Time: {MostRecent.LastWriteTime}");
                return MostRecent;
            }
            else
            {
                Console.WriteLine("No files found in the directory.");
                return null;
            }
        }

        public string[] ReadViaShadowCopy(string filePath)
        {
            string tempPath = System.IO.Path.GetTempFileName();

            try
            {
                File.Copy(filePath, tempPath, overwrite: true);
                return File.ReadAllLines(tempPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        public bool CheckIfProcessExists(int PID)
        {
            return Process.GetProcesses().Any(
                Proc => Proc.Id == PID
            );
        }

        public Dictionary<string, string>? GetRobloxDetails(string[] Lines)
        {
            foreach (string line in Lines)
            {
                if (line.Contains("game_join_loadtime"))
                {
                    // Use a regex to extract universeid and userid
                    var match = Regex.Match(line, @"universeid:(\d+),.*userid:(\d+)");
                    if (match.Success)
                    {
                        string universeId = match.Groups[1].Value;
                        string userId = match.Groups[2].Value;

                 
                        Dictionary<string, string> Data = new()
                        {
                            { "Universe", universeId },
                            { "UserID", userId }
                        };
                        return Data;
                    }
                }       
            }
            return null;
        }

        // UI animations taken from MainDab
        public void Fade(DependencyObject ElementName, double Start, double End, double Time)
        {
            DoubleAnimation Anims = new DoubleAnimation()
            {
                From = Start,
                To = End,
                Duration = TimeSpan.FromSeconds(Time),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(Anims, ElementName);
            Storyboard.SetTargetProperty(Anims, new PropertyPath(OpacityProperty));
            Storyboard storyboard = new Storyboard();
            storyboard.Children.Add(Anims);
            storyboard.Begin();
        }

        public void Move(DependencyObject ElementName, Thickness Origin, Thickness Location, double Time)
        {
            ThicknessAnimation Anims = new ThicknessAnimation()
            {
                From = Origin,
                To = Location,
                Duration = TimeSpan.FromSeconds(Time),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(Anims, ElementName);
            Storyboard.SetTargetProperty(Anims, new PropertyPath(MarginProperty));
            Storyboard storyboard = new Storyboard();
            storyboard.Children.Add(Anims);
            storyboard.Begin();
        }

        // ---------------- //
        // ** MAIN LOGIC ** //
        // ---------------- //

        // When Roblox new instance
        void ProcessWatch()
        {
            ManagementEventWatcher StartWatch = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
            StartWatch.EventArrived += new EventArrivedEventHandler(ProcessWatchEvent);
            StartWatch.Start();
        }

        // When Roblox detected
        public bool Debounce = false;
        public void ProcessWatchEvent(object sender, EventArrivedEventArgs e)
        {
            // 2nd task is valid Roblox
            Console.WriteLine("Process started: {0}", e.NewEvent.Properties["ProcessName"].Value);

            if ((string)e.NewEvent.Properties["ProcessName"].Value == "RobloxPlayerBeta.exe")
            {
                if (Debounce)
                {
                    Debounce = false;
                    Console.WriteLine("Roblox started");

                    Console.WriteLine(Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value));

                    Task.Run(async () =>
                    {
                        // is this correct?
                        await CheckMonitorLog(Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value));
                    });
                }
                else
                {
                    Debounce = true;
                }
            }
            else
            {
            }
        }
        public async Task CheckMonitorLog(int RobloxProcessID)
        { 
            for (int i = 0; i < 30; i++)
            {
                // new file detect
                FileInfo MostRecent = MostRecentRobloxLogFile();
                if (MostRecent.FullName != Last.FullName)
                {
                    Console.WriteLine("CheckMonitorLog found Roblox file: " + MostRecent.FullName);
                    Last = MostRecentRobloxLogFile();
                    _ = ReadFromLog(MostRecent.FullName, RobloxProcessID);
                    break;
                }
                Thread.Sleep(500);
            }
        }

        public async Task ReadFromLog(string File, int RobloxProcessID)
        {
            string GameName = "Failed to obtain";
            string DisplayName = "Failed to obtain";
            string RobloxUsername = "Failed to obtain";
            string RobloxAvatarUrl = "Failed";
            bool RobloxIsClose = false;
            bool ObtainedRobloxDetails = false;
            int UIIndex = 900000; // all the way at the top

            while (!RobloxIsClose)
            {
                if (CheckIfProcessExists(RobloxProcessID))
                {
                    Console.WriteLine("Reading " + File);

                    string[] Data = ReadViaShadowCopy(File); // not an actual shadow copy btw??? /praesidia

                    if (!ObtainedRobloxDetails)
                    {
                        try
                        {
                            Dictionary<string, string> RobloxDetails = GetRobloxDetails(Data);

                            if (RobloxDetails != null)
                            {
                                string UniverseID = RobloxDetails["Universe"];
                                string RobloxID = RobloxDetails["UserID"];

                                HttpClient RobloxAPI = new();

                                // universe -> place name
                                try
                                {
                                    HttpResponseMessage UniverseRes = await RobloxAPI.GetAsync($"https://games.roblox.com/v1/games?universeIds={UniverseID}");
                                    string UniverseResString = await UniverseRes.Content.ReadAsStringAsync();
                                    JObject UniverseJson = JObject.Parse(UniverseResString);
                                    Console.WriteLine(UniverseResString);
                                    GameName = UniverseJson["data"][0]["name"].ToString();
                                }
                                catch (Exception ex){ Console.WriteLine($"Error when using Roblox API for place name: {ex}"); }

                                // user id -> username
                                try
                                {
                                    HttpResponseMessage UsernameRes = await RobloxAPI.GetAsync($"https://users.roblox.com/v1/users/{RobloxID}");
                                    string UsernameResString = await UsernameRes.Content.ReadAsStringAsync();
                                    JObject UsernameJson = JObject.Parse(UsernameResString);
                                    DisplayName = UsernameJson["displayName"].ToString();
                                    RobloxUsername = UsernameJson["name"].ToString();
                                }
                                catch (Exception ex) { Console.WriteLine($"Error when using Roblox API for username: {ex}"); }

                                // user id -> avatar img
                                try
                                {
                                    HttpResponseMessage AvatarRes = await RobloxAPI.GetAsync($"https://thumbnails.roblox.com/v1/users/avatar-headshot?size=150x150&format=png&userIds={RobloxID}");
                                    string AvatarResString = await AvatarRes.Content.ReadAsStringAsync();
                                    JObject AvatarJson = JObject.Parse(AvatarResString);
                                    RobloxAvatarUrl = AvatarJson["data"][0]["imageUrl"].ToString();
                                }
                                catch (Exception ex) { Console.WriteLine($"Error when using Roblox API for avatar: {ex}"); }

                                Console.WriteLine($"Final detail: game name {GameName} display {DisplayName} username {RobloxUsername} avatar {RobloxAvatarUrl}");

                                this.Dispatcher.Invoke(() =>
                                {
                                    Console.WriteLine("*** NOW ADDING TO UI ***");

                                    // remove any previous

                                    // add to UI
                                    RobloxInstance NewInstance = new RobloxInstance();
                                    this.WP1.Children.Add(NewInstance);
                                    UIIndex = WP1.Children.IndexOf(NewInstance);

                                    // button
                                    void KillRoblox()
                                    {
                                        Console.WriteLine("Killing process " + RobloxProcessID);
                                        try
                                        {
                                            Process Rblx = Process.GetProcessById(RobloxProcessID);
                                            Rblx.Kill();
                                        }
                                        catch (Exception ex) { Console.WriteLine(ex); this.WP1.Children.Remove(NewInstance); }
                                    }

                                    NewInstance.KilInstance.Click += (_, _) => KillRoblox();

                                    NewInstance.DisplayName.Content = DisplayName;
                                    NewInstance.FullUsername.Content = RobloxUsername;
                                    NewInstance.GameName.Content = GameName;

                                    try
                                    {
                                        BitmapImage bitmap = new BitmapImage();
                                        bitmap.BeginInit();
                                        bitmap.UriSource = new Uri(@RobloxAvatarUrl, UriKind.Absolute);
                                        bitmap.EndInit();
                                        NewInstance.PFP.Source = bitmap;
                                    }
                                    catch { }

                                    Fade(NewInstance.PFP, 1, 0, 0);
                                    Fade(NewInstance.DisplayName, 1, 0, 0);
                                    Fade(NewInstance.FullUsername, 1, 0, 0);
                                    Fade(NewInstance.GameName, 1, 0, 0);
                                    Fade(NewInstance.KilInstance, 1, 0, 0);

                                    Task.Delay(100);
                                    Fade(NewInstance.PFP, 0, 1, 0.5);
                                    Move(NewInstance.PFP, new Thickness(NewInstance.PFP.Margin.Left, NewInstance.PFP.Margin.Top - 20, NewInstance.PFP.Margin.Right, NewInstance.PFP.Margin.Bottom), NewInstance.PFP.Margin, 0.75);
                                     Task.Delay(100);
                                    Fade(NewInstance.DisplayName, 0, 1, 0.5);
                                    Move(NewInstance.DisplayName, new Thickness(NewInstance.DisplayName.Margin.Left, NewInstance.DisplayName.Margin.Top - 20, NewInstance.DisplayName.Margin.Right, NewInstance.DisplayName.Margin.Bottom), NewInstance.DisplayName.Margin, 0.75);
                                     Task.Delay(100);
                                    Fade(NewInstance.FullUsername, 0, 1, 0.5);
                                    Move(NewInstance.FullUsername, new Thickness(NewInstance.FullUsername.Margin.Left, NewInstance.FullUsername.Margin.Top - 20, NewInstance.FullUsername.Margin.Right, NewInstance.FullUsername.Margin.Bottom), NewInstance.FullUsername.Margin, 0.75);
                                     Task.Delay(100);
                                    Fade(NewInstance.GameName, 0, 1, 0.5);
                                    Move(NewInstance.GameName, new Thickness(NewInstance.GameName.Margin.Left, NewInstance.GameName.Margin.Top - 20, NewInstance.GameName.Margin.Right, NewInstance.GameName.Margin.Bottom), NewInstance.GameName.Margin, 0.75);
                                     Task.Delay(100);
                                    Fade(NewInstance.KilInstance, 0, 1, 0.5);
                                    Move(NewInstance.KilInstance, new Thickness(NewInstance.KilInstance.Margin.Left, NewInstance.KilInstance.Margin.Top - 20, NewInstance.KilInstance.Margin.Right, NewInstance.KilInstance.Margin.Bottom), NewInstance.KilInstance.Margin, 0.75);

                                    Console.WriteLine("*** ADDED TO UI! ***");


                                });

                                ObtainedRobloxDetails = true;
                            }
                            else
                            {
                                Console.WriteLine("Unable to find string in log");
                            }
                        }
                        catch(Exception ex)
                        {
                            Console.WriteLine("Error attempting to get Roblox API data: " + ex);
                        }
                    }
                }
                else
                {
                    RobloxIsClose = true;
                }

                Thread.Sleep(1000);
            }

            Console.WriteLine("Roblox process closed");

            this.Dispatcher.Invoke(() =>
            {
                try{ this.WP1.Children.RemoveAt(UIIndex); }
                catch { }
            });
        }
        public string? cookies_path { get; set; }
        public MainWindow()
        {
            InitializeComponent();
            Last = MostRecentRobloxLogFile();

            // make sure Roblox is installed
            if (!Directory.Exists(RobloxPath))
            {

                MessageBox.Show("Roblox does not exist / Roblox cannot be found", "Roblox needs to be installed in C:\\Users\\[YourWindowsUsername]\\AppData\\Local\\Roblox. Multiple Roblox Instances could not find this folder. Make you are using the Roblox downloaded from Roblox (not from Microsoft Store).\n\nMultiple Roblox Instances will now close.");
                Process.Start("https://www.roblox.com/download");
                Environment.Exit(0);
            }

            // check and see if roblox is open
            if (RobloxInstancesOpen() > 0){               
                MessageBoxResult result = MessageBox.Show("Multiple Roblox Instances needs to close Roblox.", "Roblox needs to be closed before Multiple Roblox Instances starts.\n\nShould Multiple Roblox Instances close all instances of Roblox [Yes]? If [No]. Multiple Roblox Instances will close, allowing you to close Roblox manually.\n\nWARNING: If you have any unsaved data in Roblox, click [No], save your data, and close Roblox. Make sure to open Multiple Roblox Instances before starting Roblox.", MessageBoxButton.YesNo);
                if (result == MessageBoxResult.Yes)
                {
                    try { foreach (Process proc in Process.GetProcessesByName("RobloxPlayerBeta")) { proc.Kill(); } }
                    catch { }
                }
                else
                {
                    Environment.Exit(0);
                }
            }
            
            ProcessWatch();

            try { Mutex.OpenExisting("ROBLOX_singletonMutex").Close(); Mutex.OpenExisting("ROBLOX_singletonEvent").Close(); Console.WriteLine("Roblox mutex found, closing"); }
            catch{ }

            RobloxLock = new Mutex(true, "ROBLOX_singletonMutex");
            OtherRobloxLock = new Mutex(true, "ROBLOX_singletonEvent");

            try
            {
                RobloxCookieLock = new FileStream(System.IO.Path.Combine(RobloxPath, "LocalStorage", "RobloxCookies.dat"), FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to apply Error 773 fix: {ex}. You may not be able to teleport between places.");
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void Minimise_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // close locks
            try { RobloxLock.Close(); OtherRobloxLock.Close(); }
            catch { }
            Environment.Exit(0);
        }

        // Handling startup anim
        private async void Border_Loaded(object sender, RoutedEventArgs e)
        {
            // make all components transparent at first
            Fade(MW, 1, 0, 0);
            Fade(Title, 1, 0, 0);
            Fade(VersionBorder, 1, 0, 0);
            Fade(VersionText, 1, 0, 0);
            Fade(Minimise, 1, 0, 0);
            Fade(Close, 1, 0, 0);

            Fade(PlaceIDLabel, 1, 0, 0);
            Fade(PlaceIDInput, 1, 0, 0);
            Fade(PlaceIDHint, 1, 0, 0);
            Fade(FriendIDLabel, 1, 0, 0);
            Fade(FriendIDInput, 1, 0, 0);
            Fade(FriendIDHint, 1, 0, 0);
            Fade(LaunchALLButton, 1, 0, 0);
            Fade(SelectCookies, 1, 0, 0);
            Fade(AntiAFKCheckBox, 1, 0, 0);
            Fade(ChatLabel, 1, 0, 0);
            Fade(ChatMessageInput, 1, 0, 0);
            Fade(SendChatButton, 1, 0, 0);
            Fade(AutoRepeatCheckBox, 1, 0, 0);

            Fade(StatusText, 1, 0, 0);

            await Task.Delay(100);
            Fade(MW, 0, 1, 1);
            await Task.Delay(100);
            Fade(Title, 0, 1, 0.5);
            Move(Title, new Thickness(Title.Margin.Left, Title.Margin.Top - 30, Title.Margin.Right, Title.Margin.Bottom), Title.Margin, 0.75);
            await Task.Delay(100);
            Fade(VersionBorder, 0, 1, 0.5);
            Move(VersionBorder, new Thickness(VersionBorder.Margin.Left, VersionBorder.Margin.Top - 30, VersionBorder.Margin.Right, VersionBorder.Margin.Bottom), VersionBorder.Margin, 0.75);
            Fade(VersionText, 0, 1, 0.5);
            Move(VersionText, new Thickness(VersionText.Margin.Left, VersionText.Margin.Top - 30, VersionText.Margin.Right, VersionText.Margin.Bottom), VersionText.Margin, 0.75);
            await Task.Delay(100);
            Fade(Minimise, 0, 1, 0.5);
            Move(Minimise, new Thickness(Minimise.Margin.Left, Minimise.Margin.Top - 30, Minimise.Margin.Right, Minimise.Margin.Bottom), Minimise.Margin, 0.75);
            await Task.Delay(100);
            Fade(Close, 0, 1, 0.5);
            Move(Close, new Thickness(Close.Margin.Left, Close.Margin.Top - 30, Close.Margin.Right, Close.Margin.Bottom), Close.Margin, 0.75);
            await Task.Delay(100);
            Fade(PlaceIDLabel, 0, 1, 0.5);
            Move(PlaceIDLabel, new Thickness(PlaceIDLabel.Margin.Left, PlaceIDLabel.Margin.Top - 30, PlaceIDLabel.Margin.Right, PlaceIDLabel.Margin.Bottom), PlaceIDLabel.Margin, 0.75);
            Fade(PlaceIDInput, 0, 1, 0.5);
            Move(PlaceIDInput, new Thickness(PlaceIDInput.Margin.Left, PlaceIDInput.Margin.Top - 30, PlaceIDInput.Margin.Right, PlaceIDInput.Margin.Bottom), PlaceIDInput.Margin, 0.75);
            Fade(PlaceIDHint, 0, 1, 0.5);
            Move(PlaceIDHint, new Thickness(PlaceIDHint.Margin.Left, PlaceIDHint.Margin.Top - 30, PlaceIDHint.Margin.Right, PlaceIDHint.Margin.Bottom), PlaceIDHint.Margin, 0.75);
            Fade(FriendIDLabel, 0, 1, 0.5);
            Move(FriendIDLabel, new Thickness(FriendIDLabel.Margin.Left, FriendIDLabel.Margin.Top - 30, FriendIDLabel.Margin.Right, FriendIDLabel.Margin.Bottom), FriendIDLabel.Margin, 0.75);
            Fade(FriendIDInput, 0, 1, 0.5);
            Move(FriendIDInput, new Thickness(FriendIDInput.Margin.Left, FriendIDInput.Margin.Top - 30, FriendIDInput.Margin.Right, FriendIDInput.Margin.Bottom), FriendIDInput.Margin, 0.75);
            Fade(FriendIDHint, 0, 1, 0.5);
            Move(FriendIDHint, new Thickness(FriendIDHint.Margin.Left, FriendIDHint.Margin.Top - 30, FriendIDHint.Margin.Right, FriendIDHint.Margin.Bottom), FriendIDHint.Margin, 0.75);
            Fade(LaunchALLButton, 0, 1, 0.5);
            Move(LaunchALLButton, new Thickness(LaunchALLButton.Margin.Left, LaunchALLButton.Margin.Top - 30, LaunchALLButton.Margin.Right, LaunchALLButton.Margin.Bottom), LaunchALLButton.Margin, 0.75);
            Fade(SelectCookies, 0, 1, 0.5);
            Move(SelectCookies, new Thickness(SelectCookies.Margin.Left, SelectCookies.Margin.Top - 30, SelectCookies.Margin.Right, SelectCookies.Margin.Bottom), SelectCookies.Margin, 0.75);
            Fade(AntiAFKCheckBox, 0, 1, 0.5);
            Move(AntiAFKCheckBox, new Thickness(AntiAFKCheckBox.Margin.Left, AntiAFKCheckBox.Margin.Top - 30, AntiAFKCheckBox.Margin.Right, AntiAFKCheckBox.Margin.Bottom), AntiAFKCheckBox.Margin, 0.75);
            await Task.Delay(100);
            Fade(ChatLabel, 0, 1, 0.5);
            Move(ChatLabel, new Thickness(ChatLabel.Margin.Left, ChatLabel.Margin.Top - 30, ChatLabel.Margin.Right, ChatLabel.Margin.Bottom), ChatLabel.Margin, 0.75);
            Fade(ChatMessageInput, 0, 1, 0.5);
            Move(ChatMessageInput, new Thickness(ChatMessageInput.Margin.Left, ChatMessageInput.Margin.Top - 30, ChatMessageInput.Margin.Right, ChatMessageInput.Margin.Bottom), ChatMessageInput.Margin, 0.75);
            Fade(SendChatButton, 0, 1, 0.5);
            Move(SendChatButton, new Thickness(SendChatButton.Margin.Left, SendChatButton.Margin.Top - 30, SendChatButton.Margin.Right, SendChatButton.Margin.Bottom), SendChatButton.Margin, 0.75);
            Fade(AutoRepeatCheckBox, 0, 1, 0.5);
            Move(AutoRepeatCheckBox, new Thickness(AutoRepeatCheckBox.Margin.Left, AutoRepeatCheckBox.Margin.Top - 30, AutoRepeatCheckBox.Margin.Right, AutoRepeatCheckBox.Margin.Bottom), AutoRepeatCheckBox.Margin, 0.75);




            await Task.Delay(100);
            Fade(StatusText, 0, 1, 0.5);
            Move(StatusText, new Thickness(StatusText.Margin.Left, StatusText.Margin.Top + 17, StatusText.Margin.Right, StatusText.Margin.Bottom), StatusText.Margin, 0.75);
            await Task.Delay(100);
            }

        private void SelectCookies_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Title = "Select a file",
                Filter = "All files (*.*)|*.*" 
            };

            if (openFileDialog.ShowDialog() == true)
            {
                cookies_path = openFileDialog.FileName;
                Debug.WriteLine("Selected file: " + cookies_path);
                if (cookies_path != null) {
                    SelectCookies.Content = "Reselect Cookies";
                }
            }
        }

        public async Task handle_launch()
        {
            if (cookies_path == null)
            {
                MessageBox.Show("Please select cookies first.");
                return;
            }
            if (PlaceIDInput.Text == "..." && FriendIDInput.Text == "...")
            {
                MessageBox.Show("Please fill in friend id or place id first.");
                return;
            }
            if (PlaceIDInput.Text == "" && FriendIDInput.Text == "")
            {
                MessageBox.Show("Please fill in friend id or place id first.");
                return;
            }

            string[] cookies = File.ReadAllLines(cookies_path);

            // If you want a List<string>:
            var cookies_list = new List<string>(cookies);

            foreach (var cookie in cookies_list)
            {
                await Authentication.LaunchWithCookie(cookie, PlaceIDInput.Text, FriendIDInput.Text);
            }
        }
        private void LaunchALLButton_Click(object sender, RoutedEventArgs e)
        {
            handle_launch();
        }

        private void AntiAFKCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            AntiAFK.AntiAFKLoop(this); 
        }

        private async void SendChatButton_Click(object sender, RoutedEventArgs e)
        {
            string message = ChatMessageInput.Text;
            if (string.IsNullOrWhiteSpace(message) || message == "Type message here...")
            {
                MessageBox.Show("Please enter a message to send.");
                return;
            }

            await AutoChat.SendChatToAllRoblox(message);
            StatusText.Content = "Status: Sent message to all instances.";
        }

        private void AutoRepeatCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            string message = ChatMessageInput.Text;
            if (string.IsNullOrWhiteSpace(message) || message == "Type message here...")
            {
                MessageBox.Show("Please enter a message before enabling auto-repeat.");
                AutoRepeatCheckBox.IsChecked = false;
                return;
            }

            Task.Run(() => AutoChat.AutoChatLoop(this, message));
            StatusText.Content = "Status: Auto-chat enabled (every 10s).";
        }

        private void AutoRepeatCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            StatusText.Content = "Status: Auto-chat disabled.";
        }
    }
}