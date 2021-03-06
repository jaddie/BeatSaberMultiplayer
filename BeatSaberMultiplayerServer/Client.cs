﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using ServerCommons.Misc;

namespace BeatSaberMultiplayerServer
{
    internal class Client
    {
        public TcpClient _client;
        public PlayerInfo playerInfo;

        int playerScore;
        ulong playerId;
        string playerName;

        public ClientState state = ClientState.Disconnected;

        Thread _clientLoopThread;

        public Queue<string> sendQueue = new Queue<string>();

        public Client(TcpClient client)
        {
            _client = client;

            _clientLoopThread = new Thread(ClientLoop) { IsBackground = true };
            _clientLoopThread.Start();
        }

        void ClientLoop()
        {
            int pingTimer = 0;

            Logger.Instance.Log("Client connected!");

            while (_clientLoopThread.IsAlive)
            {
                try
                {
                    if (_client != null && _client.Connected)
                    {
                        pingTimer++;
                        if (pingTimer > 180)
                        {
                            SendToClient(JsonConvert.SerializeObject(new ServerCommand(ServerCommandType.Ping)));
                            pingTimer = 0;
                        }

                        string[] commands = ReceiveFromClient();

                        if (commands != null)
                        {
                            foreach (string data in commands)
                            {
                                ClientCommand command = JsonConvert.DeserializeObject<ClientCommand>(data);

                                if (command.version != Assembly.GetEntryAssembly().GetName().Version.ToString())
                                {
                                    state = ClientState.UpdateRequired;
                                    SendToClient(JsonConvert.SerializeObject(new ServerCommand(ServerCommandType.UpdateRequired)));
                                    return;
                                }

                                if (state != ClientState.Playing && state != ClientState.Connected)
                                {
                                    state = ClientState.Connected;
                                }

                                switch (command.commandType)
                                {
                                    case ClientCommandType.SetPlayerInfo:
                                        {
                                            PlayerInfo receivedPlayerInfo =
                                                JsonConvert.DeserializeObject<PlayerInfo>(command.playerInfo);
                                            if (receivedPlayerInfo != null)
                                            {
                                                if (ServerMain.serverState == ServerState.Playing)
                                                {
                                                    state = ClientState.Playing;
                                                }
                                                if (playerId == 0)
                                                {
                                                    playerId = receivedPlayerInfo.playerId;
                                                    if (!ServerMain.clients.Contains(this))
                                                    {
                                                        ServerMain.clients.Add(this);
                                                        Logger.Instance.Log("New player: " + receivedPlayerInfo.playerName);
                                                    }
                                                }
                                                else if (playerId != receivedPlayerInfo.playerId)
                                                {
                                                    return;
                                                }

                                                if (playerName == null)
                                                {
                                                    playerName = receivedPlayerInfo.playerName;
                                                }
                                                else if (playerName != receivedPlayerInfo.playerName)
                                                {
                                                    return;
                                                }

                                                playerScore = receivedPlayerInfo.playerScore;

                                                playerInfo = receivedPlayerInfo;
                                            }
                                        }
                                        ;
                                        break;
                                    case ClientCommandType.GetServerState:
                                        {
                                            if (ServerMain.serverState != ServerState.Playing)
                                            {
                                                SendToClient(
                                                    JsonConvert.SerializeObject(
                                                        new ServerCommand(ServerCommandType.SetServerState)));
                                            }
                                            else
                                            {
                                                SendToClient(JsonConvert.SerializeObject(new ServerCommand(
                                                    ServerCommandType.SetServerState,
                                                    _selectedLevelID: ServerMain.availableSongs[ServerMain.currentSongIndex].levelId,
                                                    _selectedSongDuration: ServerMain.availableSongs[ServerMain.currentSongIndex].duration.TotalSeconds,
                                                    _selectedSongPlayTime: ServerMain.playTime.TotalSeconds)));
                                            }
                                        }
                                        ;
                                        break;
                                    case ClientCommandType.GetAvailableSongs:
                                        {
                                            SendToClient(JsonConvert.SerializeObject(new ServerCommand(
                                                ServerCommandType.DownloadSongs,
                                                _songs: ServerMain.availableSongs.Select(x => x.levelId).ToArray())));
                                        }
                                        ;
                                        break;
                                }
                            }
                        }
                        while (sendQueue.Count != 0)
                        {
                            SendToClient(sendQueue.Dequeue());
                        }

                    }
                    else
                    {
                        ServerMain.clients.Remove(this);
                        if (_client != null)
                        {
                            _client.Close();
                            _client = null;
                        }
                        state = ClientState.Disconnected;
                        Logger.Instance.Log("Client disconnected!");
                        return;
                    }
                }catch(Exception e)
                {
                    Logger.Instance.Warning($"CLIENT EXCEPTION: {e}");
                }

                Thread.Sleep(8);
            }
        }

        public string[] ReceiveFromClient(bool waitIfNoData = false)
        {
            if (_client.Available == 0)
            {
                if (waitIfNoData)
                {
                    try {
                        while (_client.Available == 0 && _client.Connected) {
                            Thread.Sleep(8);
                        }
                    }
                    catch (SocketException ex) {
                        Logger.Instance.Exception(ex.Message);
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }


            if (_client == null || !_client.Connected)
            {
                return null;
            }

            NetworkStream stream = _client.GetStream();

            string receivedJson;
            byte[] buffer = new byte[_client.ReceiveBufferSize];
            int length;

            length = stream.Read(buffer, 0, buffer.Length);

            receivedJson = Encoding.UTF8.GetString(buffer).Trim('\0');
            
            while (!receivedJson.EndsWith("}"))
            {
                Logger.Instance.Log("Received message is splitted, waiting for another part...");
                receivedJson += string.Join("", ReceiveFromClient(true));
            }

            string[] strBuffer = receivedJson.Replace("}{", "}#{").Split('#');

            //Console.WriteLine("Received from client: " + receivedJson);

            return strBuffer;
        }

        public bool SendToClient(string message)
        {
            if (_client == null || !_client.Connected)
            {
                return false;
            }

            //Console.WriteLine("Sending to client: "+message);

            byte[] buffer = Encoding.UTF8.GetBytes(message);
            try
            {
                _client.GetStream().Write(buffer, 0, buffer.Length);
            }
            catch (Exception e)
            {
                return false;
            }

            return true;
        }


        public void DestroyClient()
        {
            if (_client != null)
            {
                ServerMain.clients.Remove(this);
                _client.Close();
            }
        }


    }
}