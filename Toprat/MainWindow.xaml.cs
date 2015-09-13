using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Toprat
{
    public class Movie
    {
        public string Title { get; set; }
        public string Metascore { get; set; }
        public decimal MetascoreDecimal { get; set; }
        public string tomatoRating { get; set; }
        public decimal TomatoRatingDecimal { get; set; }
        public string imdbRating { get; set; }
        public decimal ImdbRatingDecimal { get; set; }
        public decimal TopRatScore { get; set; }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        static string BASE_OMDB_URL = "http://www.omdbapi.com/?";
        static string[] VIDEO_EXTENSIONS = new[] { ".mkv", ".mp4", ".avi" };
        static string[] REMOVE_STRINGS = new[] { "yify", "brrip", "720p", "1080p", "bluray", "blu-ray", "axxo", "dvdrip" };
        static decimal WEIGHTAGE_METASCORE = 0.35M;
        static decimal WEIGHTAGE_TOMATOES = 0.35M;
        static decimal WEIGHTAGE_IMDB = 0.30M;

        public ObservableCollection<Movie> Ratings { get; set; }
        public List<string> nameProcessed { get; set; }
        BackgroundWorker RatingLoader { get; set; }

        public MainWindow()
        {
            InitializeComponent();

            RatingLoader = new BackgroundWorker();
            RatingLoader.DoWork += new DoWorkEventHandler(RatingLoader_DoWork);
            RatingLoader.ProgressChanged += new ProgressChangedEventHandler(RatingLoader_ProgressChanged);
            RatingLoader.RunWorkerCompleted += new RunWorkerCompletedEventHandler(RatingLoader_RunWorkerCompleted);
            RatingLoader.WorkerReportsProgress = true;
            RatingLoader.WorkerSupportsCancellation = true;
        }

        private void Button_Go_Click(object sender, RoutedEventArgs e)
        {
            TextBox dir = TextBox_Dir;
            if (!String.IsNullOrWhiteSpace(dir.Text) && Directory.Exists(dir.Text))
            {
                Button_Go.IsEnabled = false;
                Ratings = new ObservableCollection<Movie>();
                RatingLoader.RunWorkerAsync(dir.Text);
            }
        }

        private string GetMovieName(string fileName)
        {
            string name = Regex.Replace(fileName.ToLower().Replace('.', ' '), @"(\(.*\))|(\[.*\])", ""); // Content within brackets.
            string[] nameSpl = name.Split(' ');
            int startIndex = 0, endIndex = nameSpl.Length;
            for (int i = 0; i < endIndex; i++)
            {
                if (isPartValid(nameSpl[i]))
                {
                    startIndex = i;
                    break;
                }
            }
            StringBuilder movieNameSB = new StringBuilder();
            for (int i = startIndex; i < endIndex; i++)
            {
                if (!isPartValid(nameSpl[i]))
                {
                    endIndex = i;
                    break;
                }
                movieNameSB.Append(nameSpl[i] + " ");
            }
            return movieNameSB.ToString().Trim();
        }

        private bool isPartValid(string part)
        {
            Regex rx;
            //rx = new Regex(@"\d{4}"); // Year.
            //if (rx.IsMatch(part))
            //    return false;
            rx = new Regex(@"x\d{1,3}"); // Strings like 'x264'.
            if (rx.IsMatch(part))
                return false;
            if (REMOVE_STRINGS.Contains(part))
                return false;
            return true;
        }

        void RatingLoader_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            Ratings = new ObservableCollection<Movie>(Ratings.OrderByDescending(m => m.TopRatScore));
            DataGrid_Rating.ItemsSource = Ratings;
            Button_Go.IsEnabled = true;
        }

        void RatingLoader_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            Movie movie = (Movie)e.UserState;
            Ratings.Add(movie);
            DataGrid_Rating.ItemsSource = Ratings;
        }

        void RatingLoader_DoWork(object sender, DoWorkEventArgs e)
        {
            string dir = e.Argument.ToString();
            nameProcessed = new List<string>();
            DirectoryInfo d = new DirectoryInfo(dir);
            IEnumerable<string> files = d.GetFiles("*.*", SearchOption.AllDirectories).Where(f => VIDEO_EXTENSIONS.Contains(f.Extension.ToLower())).Select(f => Path.GetFileNameWithoutExtension(f.Name));
            int count = files.Count();
            using (StreamWriter w = File.AppendText("log.txt"))
            {
                w.WriteLine(DateTime.Now);
                for (int i = 0; i < count; i++)
                {
                    string fileName = files.ElementAt(i);
                    w.WriteLine(i + ": " + fileName);
                    string movieName = GetMovieName(fileName);
                    if (!string.IsNullOrWhiteSpace(movieName) && !nameProcessed.Contains(movieName))
                    {
                        using (var webClient = new System.Net.WebClient())
                        {
                            string json = webClient.DownloadString(BASE_OMDB_URL + "t=" + movieName + "&r=json&tomatoes=true");
                            Movie movie = JsonConvert.DeserializeObject<Movie>(json);
                            if (movie != null && !string.IsNullOrWhiteSpace(movie.Title))
                            {
                                decimal score = 0;
                                decimal.TryParse(movie.Metascore, out score);
                                movie.MetascoreDecimal = score;
                                decimal weightMeta = score > 0 ? WEIGHTAGE_METASCORE : 0;

                                score = 0;
                                decimal.TryParse(movie.tomatoRating, out score);
                                movie.TomatoRatingDecimal = score;
                                decimal weightTomato = score > 0 ? WEIGHTAGE_TOMATOES : 0;

                                score = 0;
                                decimal.TryParse(movie.imdbRating, out score);
                                movie.ImdbRatingDecimal = score;
                                decimal weightImdb = score > 0 ? WEIGHTAGE_IMDB : 0;

                                decimal weightTots = weightMeta + weightTomato + weightImdb;
                                if(weightTots > 0)
                                    movie.TopRatScore = Math.Round((weightMeta * movie.MetascoreDecimal
                                        + weightTomato * movie.TomatoRatingDecimal * 10 + weightImdb * movie.ImdbRatingDecimal * 10) / weightTots);
                                RatingLoader.ReportProgress(0, movie);
                                w.WriteLine(movie.Title);
                            }
                        }
                        nameProcessed.Add(movieName);
                        w.WriteLine(movieName);
                    }
                }
                w.WriteLine();
            }
        }

        private static string stripBeginning(string name)
        {
            List<Regex> rxList = new List<Regex>();
            rxList.Add(new Regex(@"(\(.*\))|(\[.*\])")); // Content within brackets.
            rxList.Add(new Regex(@"(.+\d{4})|(\d{4}.+)")); // Year preceeded or followed by full stop.
            rxList.Add(new Regex(@"x\d{1,3}")); // Strings like 'x264'.

            while (true)
            {
                int index = -1;
                foreach (Regex rx in rxList)
                {
                    Match rxM = rx.Match(name);
                    index = rxM.Index;
                    if (index == 0)
                    {
                        name = rx.Replace(name, "");
                        break;
                    }
                }
                if (index != 0)
                    break;
            }
            return name;
        }

        private static string stripEnd(string name)
        {
            List<Regex> rxList = new List<Regex>();
            while (true)
            {
                int index = -1;
                foreach (Regex rx in rxList)
                {
                    Match rxM = rx.Match(name);
                    index = rxM.Index;
                    if (index == 0)
                    {
                        name = rx.Replace(name, "");
                        break;
                    }
                }
                if (index != 0)
                    break;
            }
            return name;
        }
    }
}