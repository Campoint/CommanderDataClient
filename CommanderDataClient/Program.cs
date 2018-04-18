using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Codeplex.Data;

namespace CommanderDataClient
{
    class Program
    {
        private const string Filename = @"sequenceID.txt";
        private const string accessKey = "";  // get your key from https://www.visit-x.net/interfaces/content/settings.php

        struct Key
        {
            public string type;
            public string key;
            public int version;
            public override string ToString() => $"{type}.{version}.{key}";
        }

        // store this in your database
        private readonly ConcurrentDictionary<Key, string> _database = new ConcurrentDictionary<Key, string>(); // key, json
        private CancellationTokenSource _cancelToken;

        private int _sequenceID;

        static void Main()
        {
            var p = new Program();
            var task = p.Start();
            p.LoopPrintAllOnline(); // endless loop
            task.Wait(); // endless loop
        }

        private void LoopPrintAllOnline()
        {
            while (true)
            {
                foreach (var kv in from it in _database select it)
                {
                    Console.WriteLine($"key: {kv.Key} data:{kv.Value}");
                }
                Console.WriteLine("press enter");
                Console.ReadLine();
            }
        }

        // when your task did not run for a time the websocket returns all changes beginning from a given sequenceID. 
        // so you won't miss anything. 
        private async Task Start()
        {
            Console.WriteLine("Start()");
            LoadSequenceID(); // when you want all information again, simply set sequenceID=0 
            var bytesReceived = new ArraySegment<byte>(new byte[1024]);

            _cancelToken = new CancellationTokenSource();
            // look endless. you have a single websocket-connection and new information is posted by the server
            while (!_cancelToken.Token.IsCancellationRequested)
            {
                try
                {
                    using (var ws = new ClientWebSocket())
                    {
                        // condition to get the onlinestate only - you can ommit conditions if you want.
                        var url = $"wss://data.campoints.net/?accessKey={accessKey}&seq={_sequenceID}";
                        Console.WriteLine($"Connect({url})");
                        await ws.ConnectAsync(new Uri(url), _cancelToken.Token);

                        var sb = new StringBuilder();
                        while (!_cancelToken.IsCancellationRequested)
                        {
                            var result = await ws.ReceiveAsync(bytesReceived, _cancelToken.Token);
                            sb.Append(Encoding.UTF8.GetString(bytesReceived.Array, 0, result.Count));
                            if (result.EndOfMessage)
                            {
                                // the websocket near to real time
                                Process(sb.ToString());
                                sb.Clear();
                            }
                            if (ws.State != WebSocketState.Open)
                            {
                                Console.WriteLine("Closed: " + ws.CloseStatusDescription);
                                break;
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    Thread.Sleep(1000); // prevent trashing
                }
            }
        }

        // update your local database
        private void Process(string json)
        {
            Console.WriteLine("Process: " + json);
            try
            {
                var r = DynamicJson.Parse(json);
                var data = (string) DynamicJson.Serialize(r.data);
                var key = new Key {key = r.key, type = r.type, version = (int)r.version};
                var deleted = (bool)r.deleted;

                if (!String.Equals(key.type, "keepalive"))  // there is a keepalive-response to prevent websocket timeouts
                {
                    if (deleted)
                    {
                        string del;
                        _database.TryRemove(key, out del);
                        Console.WriteLine($"delete {key}");
                    }
                    else
                    {
                        _database.AddOrUpdate(key, data, (i, b) => data);
                        Console.WriteLine($"AddOrUpdate {key}");
                    }
                }

                var seq = (int)r.seq;
                SaveSequenceID(seq);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        // Load from a database. When you want to get all information again, simply set the sequenceID=0
        private void LoadSequenceID()
        {
            Console.WriteLine("Loading sequenceID");
            try
            {
                _sequenceID = int.Parse(File.ReadAllText(Filename));
                Console.WriteLine("Loaded sequenceID:" + _sequenceID);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message, ex);
            }
        }

        // store persistent. database may be a better place
        private void SaveSequenceID(int seq)
        {
            Console.WriteLine("Save sequenceID:" + seq);
            File.WriteAllText(Filename, seq.ToString());
            _sequenceID = seq;
        }
    }
}