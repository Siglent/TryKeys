/*
TryKeys - a .NET Core 2.1 utility - Companion application to FindKeys  

The purpose of this utility is to recover the valid keys you licensed with your scope but you lost the paperwork for and 
you do not remember the codes for. 

First you generate a list of possible keys using the FindKeys utility. Edit the output of FindKeys to remove any keys which 
are not likely candidates (e.g. real life words). Then, execute this utility using that file as an input source.

Upon startup, the program reads the contents of "TryKeys.json" to configure various parameters needed for it to work. The 
options in this file and their purpose are as follows:

keyfile: The fully-qualified path to the list of keys you wish to try, e.g. "g:trykeys.txt"

scopeip: the IP address of the scope, needed for web and telnet access

port: the telnet port the program should connect to. Default '23'.

username: The telnet username. Default is "root".

password: The telnet password. Default is "eevblog"

bandwidth: Tells the program to not only attempt to find any missing option licenses, but also the maximum bandwidth license.


Theory of operation:

The program cycles through a list of keys contained in the key file. For option licenses, it issues the "license install" 
SCPI command through the web interface. It uses the telnet connection to determine if the option license file was created 
in /usr/bin/siglent/firmdata0 after issuing the command. If the file exists, then the key used for the license install 
command was the 'correct' one.

For bandwidth licenses, the program determines the current bandwidth license key from the firmdata0 directory, and what 
that key is good for, by using the PRBD SCPI command. Then, it cycles through the keys, issuing the MCBD with they test 
key, and then re-examines the output of PRBD to determine if the bandwidth has changed. If so, it determines if the 
bandwidth increased -- in which case, it will check to see if the maximum bandwidth has been reached with the key. If the 
bandwith decreased, then it re-issues the MCBD commmand to 're-install' the 'current' bandwidth license key so scope 
bandwidth will not decrease.

A log is dumped to the console. Upon program completion, the scope will be restarted if the bandwidth was changed. This 
is necessary for the new bandwidth to take effect. Finally, a summary of license keys located will be printed. 

To execute from the command line:   dotnet TryKeys.dll  

Sample log file:

Execution starts @ 10/4/2018 8:58 PM  
Scope Option 'AWG' not licensed, will seek key  
Scope Option 'MSO' not licensed, will seek key  
Scope Option 'WIFI' not licensed, will seek key  
Scope bandwidth license key: VVVVVVVVVVVVVVV  
We have 584 keys to try for 4 options  
Scope bandwidth currently licensed: 50M of 200M  
100M Bandwidth license key found: 1111111111111111  
Maximum bandwidth (200M) license key found: 2222222222222222  
Scope Option 'AWG' license key found: AAAAAAAAAAAAAAAA  
Scope Option 'MSO' license key found: MMMMMMMMMMMMMMMM  
Scope Option 'WIFI' license key found: WWWWWWWWWWWWWWWW  
  
Summary of License keys located:  
200M bandwidth license key: 2222222222222222  
AWG license key: AAAAAAAAAAAAAAAA  
MSO license key: MMMMMMMMMMMMMMMM  
WIFI license key: WWWWWWWWWWWWWWWW  
  
Rebooting scope to activate higher bandwidth license.  
  
Execution ends @ 10/4/2018 9:03 PM  

You can verify the presence of your recovered license keys on the scope's 'options' screen. You should 
print a copy of your recovered license keys and keep them in a safe place for future reference.

To revert the scope back to the previous bandwidth license, and to remove the optional licenses, you 
execute the following script after logging in via a telnet session as root:

mount -o remount,rw /usr/bin/siglent/firmdata0  
rm /usr/bin/siglent/firmdata0/options*  
cat VVVVVVVVVVVVVVVV > /usr/bin/siglent/firmdata0  
(control-d)(control-d)  
sync  
reboot  


This program has several dependencies you must install through the NuGet Package Manager.  
They are: Microsoft.Extensions.Configuration, Newtonsoft.Json, and Telnet (from 9swampy).

Note: at the moment this utility only supports the SDS####X-E series of scopes, however, additional  
functionality will be added as details become available.

Special thanks to eevblog user tv84 who gave me tons of assistance during the development of this utility.
*/
using System;
using System.Collections;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Newtonsoft.Json;
using PrimS.Telnet;
using System.Threading;
using System.Threading.Tasks;

namespace TryKeys
{
    static class Program
    {
        static void Main(string[] args)
        {
            TelnetClient client;
            ScopeResult json;
            String s_filename = String.Empty;
            String body = String.Empty;
            String s_ip = String.Empty;
            String s_username = String.Empty;
            String s_password = String.Empty;
            String result = String.Empty;
            String s_Bandwidth = String.Empty;
            Int32 i = 0;
            Int32 i_bandwidth = -1;
            Int32 i_curbandwidth = -1;
            Int32 i_port = 23;
            Int32 i_maxbandwidth = -1;
            Boolean b_reboot = false;
            List<String> keys;
            List<String> options = new List<String>();
            Dictionary<String, String> found = new Dictionary<String, String>();

            IConfigurationRoot config = new ConfigurationBuilder().AddJsonFile("TryKeys.json", false, true).Build();

            if (config["keyfile"].Length > 0)
            {
                if (!File.Exists(config["keyfile"]))
                {
                    Console.WriteLine("ERROR: Key File not found (wrong file specified in configuration file?)");
                    return;
                }
                else
                    keys = new List<String>(File.ReadAllLines(config["keyfile"]));
            }
            else
            {
                Console.WriteLine("ERROR: Key File not specified in configuration file.");
                return;
            }

            if ( config["bandwidth"] == "true" )
            {
                options.Add("MCBD");
                if (!Int32.TryParse(config["maxbandwidth"].Length > 0 ? config["maxbandwidth"] : "-1", out i_maxbandwidth) || i_maxbandwidth == -1 )
                {
                    Console.WriteLine("ERROR: Invalid maximum bandwidth specified in configuration file");
                    return;
                }
            }

            s_ip = config["scopeip"];
            if (s_ip.Length == 0)
            {
                Console.WriteLine("ERROR: Invalid telnet IP address specified in configuration file");
                return;
            }

            if (!Int32.TryParse(config["port"].Length > 0 ? config["port"] : "23", out i_port))
            {
                Console.WriteLine("ERROR: Invalid telnet port specified in configuration file");
                return;
            }

            s_username = config["username"].Length > 0 ? config["username"] : "root";
            s_password = config["password"].Length > 0 ? config["password"] : "eevblog";

            Console.WriteLine("Execution starts @ " + DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString());

            client = new TelnetClient(s_ip, 23);
            if (!client.Login(s_username, s_password))
            {
                Console.WriteLine("ERROR: Unable to establish telnet connection on port 23 to scope.");
                return;
            }

            foreach (String option in new String[] { "AWG", "MSO", "WIFI" })
            {
                s_filename = "/usr/bin/siglent/firmdata0/options_" + option.ToLower() + "_license.txt";
                result = client.Send("cat " + s_filename);
                if (result.Contains("No such file or directory"))
                {
                    Console.WriteLine("Scope Option '{0}' not licensed, will seek key", option);
                    options.Add(option);
                }
                else
                {
                    if (result.Length > 15)
                    {
                        found[option] = result.Substring(0, 16);
                        Console.WriteLine("Scope Option '{0}' already licensed: {1}", option, found[option]);
                    }
                    else
                        Console.WriteLine("Unknown response from telnet query:\n{0}", result);
                }
            }

            s_filename = "/usr/bin/siglent/firmdata0/bandwidth.txt";
            result = client.Send("cat " + s_filename);
            if (result.Length > 15)
            {
                s_Bandwidth = result.Substring(0, 16);
                keys.Remove(s_Bandwidth);
                Console.WriteLine("Scope bandwidth license key: {0}", s_Bandwidth);
            }

            Console.WriteLine("We have {0} keys to try for {1} options", keys.Count, options.Count);

            HttpWebResponse resp = null;
            CookieCollection cookies = new CookieCollection();
            WebHeaderCollection headers = new WebHeaderCollection();
            ArrayList formdata = new ArrayList();

            resp = GetWebResponse("http://" + s_ip + "/SCPI_control.php", null, null, null, "GET", true);

            if (resp.StatusCode == HttpStatusCode.OK)
            {
                GetWebResponseBody(resp);
                cookies = resp.Cookies;
                headers.Add("Accept", "*/*");
                headers.Add("Accept-Encoding", "gzip, deflate");
                headers.Add("Accept-Language", "en-US,en;q=0.9");
                headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/69.0.3497.100 Safari/537.36");
                headers.Add("X-Requested-With", "XMLHttpRequest");
                headers.Add("Referer", "http://" + s_ip + "/SCPI_control.php");
                headers.Add("Origin", "http://" + s_ip);
                headers.Add("Host", s_ip);

                formdata = new ArrayList();
                formdata.Add(new String[] { "cmd", "" });
                formdata.Add(new String[] { "action", "excutescpicmds" });

                if (options.Contains("MCBD"))
                {
                    formdata[0] = new String[] { "cmd", "{\"cmd\":\"PRBD?\",\"type\":\"ds\",\"to\":\"127.0.0.1\"}" };
                    resp = GetWebResponse("http://" + s_ip + "/device_read_write.php", formdata, cookies, headers, "POST", false);
                    if (resp.StatusCode == HttpStatusCode.OK)
                    {
                        body = GetWebResponseBody(resp);
                        json = GetJSONResponse(body);
                        if (json.success)
                        {
                            if (json.cmdrslt.Contains("M"))
                            {
                                i_bandwidth = Convert.ToInt32(json.cmdrslt.Replace("M", ""));
                                if (i_bandwidth == i_maxbandwidth)
                                {
                                    Console.WriteLine("WARNING: Maximum bandwidth already licensed. Skipping key search request.");
                                    options.Remove("MCBD");
                                    i_bandwidth = i_maxbandwidth = 0;
                                }
                                else
                                {
                                    Console.WriteLine("Scope bandwidth currently licensed: {0}M of {1}M", i_bandwidth, i_maxbandwidth);
                                    i_curbandwidth = i_bandwidth;
                                }
                            }
                        }
                    }
                }

                foreach (String option in options)
                {
                    i = 0;
                    Console.Write("Looking for '{0}' key 00000000                 ", option);
                    foreach (String key in keys)
                    {
                        Console.Write("{0}{1:D8} {2}", new String('\b', 25), ++i, key);
                        formdata[0] = new String[] { "cmd", "{\"cmd\":\"" + ((option == "MCBD") ? ("MCBD " + key) : ("LCISL " + option + "," + key)) + "\",\"type\":\"ds\",\"to\":\"127.0.0.1\"}" };
                        resp = GetWebResponse("http://" + s_ip + "/device_read_write.php", formdata, cookies, headers, "POST", false);
                        if (resp.StatusCode == HttpStatusCode.OK)
                        {
                            body = GetWebResponseBody(resp);
                            json = GetJSONResponse(body);
                            if (json.success)
                            {
                                switch (option)
                                {
                                    case "AWG":
                                    case "MSO":
                                    case "WIFI":
                                        s_filename = "/usr/bin/siglent/firmdata0/options_" + option.ToLower() + "_license.txt";
                                        result = client.Send("cat " + s_filename);
                                        if (!result.Contains("No such file or directory"))
                                        {
                                            Console.WriteLine("{0}Scope Option '{1}' license key found: {2}", new String('\b', 44+option.Length), option, key);
                                            found[option] = key;
                                        }
                                        break;
                                    case "MCBD":
                                        formdata[0] = new String[] { "cmd", "{\"cmd\":\"PRBD?\",\"type\":\"ds\",\"to\":\"127.0.0.1\"}" };
                                        resp = GetWebResponse("http://" + s_ip + "/device_read_write.php", formdata, cookies, headers, "POST", false);
                                        if (resp.StatusCode == HttpStatusCode.OK)
                                        {
                                            body = GetWebResponseBody(resp);
                                            json = GetJSONResponse(body);
                                            if (json.success)
                                            {
                                                if (json.cmdrslt.Contains("M"))
                                                {
                                                    i_bandwidth = Convert.ToInt32(json.cmdrslt.Replace("M", ""));
                                                    switch (i_bandwidth - i_curbandwidth)
                                                    {
                                                        // new license key bandwidth is higher
                                                        case var t when t > 0:
                                                            b_reboot = true;
                                                            if (i_bandwidth == i_maxbandwidth)
                                                            {
                                                                i_curbandwidth = i_bandwidth;
                                                                s_Bandwidth = key;
                                                                found[option] = key;
                                                                Console.WriteLine("{0}Maximum bandwidth ({1}M) license key found: {2}", new String('\b', 48), i_maxbandwidth, key);
                                                            }
                                                            else
                                                            {
                                                                i_curbandwidth = i_bandwidth;
                                                                s_Bandwidth = key;
                                                                Console.WriteLine("{0}{1}M Bandwidth license key found: {2}", new String('\b', 48), i_curbandwidth, key);
                                                                Console.Write("Looking for '{0}' key {1:D8} {2}", option, ++i, key);
                                                            }
                                                            break;
                                                        // new license key is lower -- restore old key
                                                        case var t when t < 0:
                                                            formdata[0] = new String[] { "cmd", "{\"cmd\":\"MCBD " + s_Bandwidth + "\",\"type\":\"ds\",\"to\":\"127.0.0.1\"}" };
                                                            resp = GetWebResponse("http://" + s_ip + "/device_read_write.php", formdata, cookies, headers, "POST", false);
                                                            break;
                                                    }
                                                }
                                            }
                                        }
                                        break;
                                }
                            }
                        }
                        if (found.ContainsKey(option))
                        {
                            keys.Remove(option);
                            break;
                        }
                    }
                    if ( !found.ContainsKey(option))
                        Console.WriteLine("{0}not found!", new String('\b', 25));

                }
            }

            Console.WriteLine("\n\nSummary of License keys located:");
            foreach (String option in options)
            {
                switch(option)
                {
                    case "AWG":
                    case "MSO":
                    case "WIFI":
                        if ( options.Contains(option))
                            Console.WriteLine(option + " license key: " + found[option]);
                        break;
                    case "MCBD":
                        Console.WriteLine("{0}M bandwidth license key: {1}", i_curbandwidth, s_Bandwidth);
                        break;
                }
            }

            if (b_reboot)
            {
                client.Send("reboot");
                Console.WriteLine("\nRebooting scope to activate higher bandwidth license.");
            }

            Console.WriteLine("\nExecution ends @ " + DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString());
        }

        private static ScopeResult GetJSONResponse(String s_body)
        {
            return JsonConvert.DeserializeObject<ScopeResult>(s_body.Replace("\\r\\n", "").Replace("\r\n", "").Replace("}{", ","));
        }

        private static HttpWebResponse GetWebResponse(String url, ArrayList Parameters, CookieCollection cookies, WebHeaderCollection headers, String Method, Boolean allowRedirect)
        {
            String Params = String.Empty;
            if (Parameters != null)
            {
                foreach (String[] param in Parameters)
                {
                    Params += (Params.Length > 0 ? "&" : "") + String.Format("{0}={1}", param[0], WebUtility.UrlEncode(param[1]));
                }
            }
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.AllowAutoRedirect = allowRedirect;
            req.SendChunked = false;
            req.Method = Method;
            req.ServicePoint.Expect100Continue = false;
            req.KeepAlive = true;
            if (headers != null)
            {
                foreach (String key in headers.AllKeys)
                {
                    switch (key)
                    {
                        case "Accept":
                            req.Accept = headers.Get(key);
                            break;
                        case "Connection":
                            switch (headers.Get(key).ToUpper())
                            {
                                case "KEEP-ALIVE":
                                    req.KeepAlive = true;
                                    break;
                            }
                            break;
                        case "Content-Length":
                            req.ContentLength = Convert.ToInt32(headers.Get(key));
                            break;
                        case "Content-Type":
                            req.ContentType = headers.Get(key);
                            break;
                        case "Date":
                            req.Date = DateTime.Parse(headers.Get(key));
                            break;
                        case "Host":
                            req.Host = headers.Get(key);
                            break;
                        case "Referer":
                            req.Referer = headers.Get(key);
                            break;
                        case "User-Agent":
                            req.UserAgent = headers.Get(key);
                            break;
                        default:
                            if (req.Headers.AllKeys.Contains<String>(key))
                            {
                                req.Headers[key] = headers.Get(key);
                            }
                            else
                            {
                                req.Headers.Add((key == "Set-Cookie" ? "Cookie" : key), headers.Get(key));
                            }
                            break;
                    }
                }
            }
            req.CookieContainer = new CookieContainer();
            if (cookies != null)
            {
                foreach (Cookie cookie in cookies)
                {
                    req.CookieContainer.Add(cookie);
                }
            }
            if (Method == "POST")
            {
                Byte[] bytes = System.Text.Encoding.ASCII.GetBytes(Params);
                req.ContentType = "application/x-www-form-urlencoded;charset=UTF-8";
                req.ContentLength = bytes.Length;
                using (Stream os = req.GetRequestStream())
                {
                    os.Write(bytes, 0, bytes.Length);
                }
            }
            WebResponse resp = req.GetResponse();
            return (HttpWebResponse)resp;
        }

        public static String GetWebResponseBody(HttpWebResponse resp)
        {
            StreamReader sr = new StreamReader(resp.GetResponseStream());
            return sr.ReadToEnd();
        }

        private static async Task QueryScope(String s_ip)
        {
            using (Client client = new Client(s_ip, 23, new System.Threading.CancellationToken()))
            {
                if (client.IsConnected)
                {
                    await client.TryLoginAsync("root", "eevblog", 10000);
                    await client.WriteLine("ls /usr/bin/siglent/firmdata0/option*");
                    String s = await client.TerminatedReadAsync("#", TimeSpan.FromMilliseconds(10000));
                }
            }
        }
    }    
}