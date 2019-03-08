﻿using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Remote;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Utility;
using Utility.MongoApi;

namespace SteamBot.Utility.MongoApi {
    public class Fake {
        public string name;
        public string password;
        public string registrationEmail;

        public Fake(string name, string password, string registrationEmail) {
            this.name = name;
            this.password = password;
            this.registrationEmail = registrationEmail;
        }
    }

    public class FakeDatabase : GenericMongoDB<Fake> {
        public override string GetCollectionName() {
            return "fakes";
        }

        public override string GetDBName() {
            return "codeforces_main";
        }
    }

    public class FakeFactory {
        private static string RAPID_API = "b478fc7612mshc13ce45cf500611p187369jsn407c14d2589a";
        private static string RAPID_API_DOMAINS_ENDPOINT = "https://privatix-temp-mail-v1.p.rapidapi.com/request/domains/";
        private static string RAPID_API_EMAILS_ENDPOINt = "https://privatix-temp-mail-v1.p.rapidapi.com/request/mail/id/{0}/";
        private static string NICKNAME_GENERATOR_ENDPOINT = "http://foulomatic.hnldesign.nl/";
        private static string REGISTER_ENDPOINT = "https://codeforces.com/register";
        private static string KAN_COMMENTS = "https://codeforces.com/comments/with/KAN";
        private static string CHECK_ENDPOINT = "https://google.com";
        private static string _DOMAINS_CACHE = null;
        private const int HandleMIN = 3;
        private const int HandleMAX = 24;
        private static readonly Random R = new Random();

        public static string GenerateString(int len = 10) {
            string res = "";
            string can = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz1234567890";
            for (int i = 0; i < len; ++i) {
                res += can[R.Next(0, can.Length)];
            }
            return res;
        }

        static string GetMd5Hash(string input) {
            using (MD5 md5Hash = MD5.Create()) {
                byte[] data = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder sBuilder = new StringBuilder();
                for (int i = 0; i < data.Length; i++) {
                    sBuilder.Append(data[i].ToString("x2"));
                }
                return sBuilder.ToString();
            }
        }

        static string textToCamelWords(string text) {
            string[] words = text.Split(new char[] { ' ', '-', '_' });
            words = words.Select(word => word[0].ToString().ToUpper() + word.Substring(1)).ToArray();
            string res = "";
            for (int i = 0; i < words.Length; ++i) {
                res += words[i];
            }
            return res;
        }

        public static Fake CreateFake() { 
            try {

                string password = GenerateString();
                lock (RAPID_API_DOMAINS_ENDPOINT) {
                    if (_DOMAINS_CACHE == null) {
                        _DOMAINS_CACHE = Request.Get(RAPID_API_DOMAINS_ENDPOINT, new WebHeaderCollection {
                            ["X-RapidAPI-Key"] = RAPID_API
                        });
                    }
                };
                string response = _DOMAINS_CACHE;
                JArray resp = JArray.Parse(response);
                string domain = (string)resp[R.Next(resp.Count)];
                string email = GenerateString(15).ToLower() + domain;

                var cService = ChromeDriverService.CreateDefaultService();
                cService.HideCommandPromptWindow = true;

                var options = new ChromeOptions();

                options.AddArguments($"--proxy-server=localhost:1234");

                options.Proxy = null;

                string userAgent = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/72.0.3626.109 YaBrowser/19.3.0.2485 Yowser/2.5 Safari/537.36";


                string args = $"--user-agent={userAgent}$PC${{}}";
                options.AddArgument(args);
                IWebDriver _webDriver = new ChromeDriver(cService, options);
                IJavaScriptExecutor jsDriver = (IJavaScriptExecutor)_webDriver;
                _webDriver.Navigate().GoToUrl(NICKNAME_GENERATOR_ENDPOINT);
                string handle = "";
                for (int retriesLeft = 10; retriesLeft >= 0; --retriesLeft) {
                    jsDriver.ExecuteScript($"document.getElementById('generate').click();");
                    Thread.Sleep(1500);
                    string answ = (string)jsDriver.ExecuteScript($"return document.getElementById('generated').innerText;");

                    handle = textToCamelWords(answ);
                    if (handle.Length >= HandleMIN && handle.Length <= HandleMAX) {
                        break;
                    }
                }
                if (handle.Length < HandleMIN || handle.Length > HandleMAX) {
                    return null;
                }

                _webDriver.Navigate().GoToUrl(REGISTER_ENDPOINT);

                Thread.Sleep(2500);

                jsDriver.ExecuteScript($"document.getElementsByName('handle')[0].value = '{handle}';");
                jsDriver.ExecuteScript($"document.getElementsByName('email')[0].value = '{email}';");
                jsDriver.ExecuteScript($"document.getElementsByName('password')[0].value = '{password}';");
                jsDriver.ExecuteScript($"document.getElementsByName('passwordConfirmation')[0].value = '{password}';");
                jsDriver.ExecuteScript($"document.getElementsByClassName(\"submit\")[0].click();");
                Thread.Sleep(2500);
                string md5 = GetMd5Hash(email);
                string mail_text = null;
                for (int retriesLeft = 2; retriesLeft >= 0; --retriesLeft) {
                    Thread.Sleep(2500);
                    try {
                        WebHeaderCollection headers = new WebHeaderCollection {
                            ["X-RapidAPI-Key"] = RAPID_API
                        };
                        JArray messages = JArray.Parse(Request.Get(string.Format(RAPID_API_EMAILS_ENDPOINt, md5), headers));
                        mail_text = (string)messages[0]["mail_text_only"];
                        break;
                    } catch {

                    }
                }
                if (mail_text == null) {
                    return null;
                }
                // example pattern to match: https://codeforces.com/register/confirm/0c0972cb33c1658f2725d13167ac82072c2d3e66
                Regex regex = new Regex("https://codeforces.com/register/confirm/[0-9a-z]*");
                Match match = regex.Match(mail_text);
                _webDriver.Navigate().GoToUrl(match.Groups[0].Value);
                Fake res = new Fake(handle, password, email);
                FakeDatabase fakeDatabase = new FakeDatabase();
                fakeDatabase.Insert(res);

                _webDriver.Navigate().GoToUrl("https://codeforces.com/problemset/submit");
                jsDriver.ExecuteScript($"document.getElementById('toggleEditorCheckbox').click();");
                jsDriver.ExecuteScript($"document.getElementsByName('submittedProblemCode')[0].value = '869A';");
                jsDriver.ExecuteScript($"document.getElementsByName('programTypeId')[0].value = 6;");
                jsDriver.ExecuteScript($"document.getElementsByName('source')[0].value = 'Karen';");
                jsDriver.ExecuteScript($"document.getElementsByClassName('submit')[0].click();");

                _webDriver.Navigate().GoToUrl(KAN_COMMENTS);
                dynamic elements = jsDriver.ExecuteScript("return document.getElementsByClassName('info');");
                List<string> commentLinks = new List<string>();
                foreach (var el in elements) {
                    if (el is RemoteWebElement remoteWebElement) {
                        commentLinks.Add(remoteWebElement.FindElementByXPath("div/a[2]").GetAttribute("href"));
                    }
                }
                foreach (var commentLink in commentLinks) {
                    _webDriver.Navigate().GoToUrl(commentLink);
                    string[] parts = commentLink.Split('-');
                    string commentId = parts[parts.Length - 1];
                    jsDriver.ExecuteScript($"$(document.getElementsByClassName('comment-content-{commentId}')[0].parentElement).find('.vote-for-comment')[1].click()");
                    Thread.Sleep(1000);
                 }

                _webDriver.Close();
                return res;
            } catch (Exception e) {
                return null;
            }
        }
    }
}
