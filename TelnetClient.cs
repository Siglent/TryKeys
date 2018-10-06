using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PrimS.Telnet;

namespace TryKeys
{
    class TelnetClient
    {
        private Client _client;

        public Boolean Connected { get { return _client.IsConnected; } }

        public TelnetClient(String s_ip, Int32 i_port)
        {
            _client = new Client(s_ip, i_port, new CancellationToken());
        }

        public Boolean Login(String s_username, String s_password)
        {
            Task<Boolean> login = this._login(s_username, s_password);
            return login.Result;
        }

        private async Task<Boolean> _login(String s_username, String s_password)
        {
            if (_client.IsConnected)
            {
                Boolean x  = await _client.TryLoginAsync(s_username, s_password, 5000, "#");
                if (x)
                    Send("stty -echo");
                return x;
            }
            return false;
        }

        public String Send(String s_cmd)
        {
            if (_client.IsConnected)
            {
                Task<String> s = _send(s_cmd);
                return s.Result;
            }
            else
                return null;
        }

        private async Task<String> _send(String s_cmd)
        {
            await _client.WriteLine(s_cmd);
            String s = await _client.TerminatedReadAsync("#", TimeSpan.FromMilliseconds(1000));
            return s;
        }
    }
}