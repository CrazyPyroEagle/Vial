using Server;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Vial.MonitorServer
{
    class Program
    {
        private readonly byte[] ClientHeader = new byte[] { 1 };
        private readonly byte[] ServerHeader = new byte[] { 2 };

        static void Main(string[] args)
        {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Any, 36000));
            socket.Listen(1);
            Socket client;
            while (true)
            {
                client = socket.Accept();
                Console.WriteLine("Client connected");
                try
                {
                    bool isServer = false;
                    byte[] clientMessage = new byte[4096];
                    byte[] serverMessage = new byte[4096];
                    int clientIndex = 0, serverIndex = 0;
                    byte[] buffer = new byte[4096];
                    int packetBytes = 0;
                    byte[] pbBuffer = new byte[4];
                    int pbIndex = pbBuffer.Length;
                    int received;
                    while ((received = client.Receive(buffer, 0, buffer.Length, SocketFlags.None)) > 0)
                    {
                        for (int walk = 0; walk < received; walk++)
                        {
                            if (pbIndex < pbBuffer.Length)
                            {
                                pbBuffer[pbIndex++] = buffer[walk];
                                if (pbIndex == pbBuffer.Length) packetBytes = BitConverter.ToInt32(pbBuffer, 0);
                            }
                            else if (packetBytes <= 0)
                            {
                                isServer = buffer[walk] == 2;
                                pbIndex = 0;
                            }
                            else if (buffer[walk] != 0)
                            {
                                if (isServer)
                                {
                                    if (serverIndex >= serverMessage.Length)
                                    {
                                        byte[] newMsg = new byte[serverMessage.Length << 1];
                                        Array.Copy(serverMessage, 0, newMsg, 0, serverMessage.Length);
                                        serverMessage = newMsg;
                                    }
                                    serverMessage[serverIndex++] = buffer[walk];
                                }
                                else
                                {
                                    if (clientIndex >= clientMessage.Length)
                                    {
                                        byte[] newMsg = new byte[clientMessage.Length << 1];
                                        Array.Copy(clientMessage, 0, newMsg, 0, clientMessage.Length);
                                        clientMessage = newMsg;
                                    }
                                    clientMessage[clientIndex++] = buffer[walk];
                                }
                                packetBytes--;
                            }
                            else
                            {
                                int index = isServer ? serverIndex : clientIndex;
                                byte[] message = isServer ? serverMessage : clientMessage;
                                byte[] finalMessage = new byte[index];
                                while (index-- > 0) finalMessage[index] = message[index];
                                if (finalMessage.Length > 0)
                                {
                                    object messageObj = null;
                                    try
                                    {
                                        messageObj = Activator.CreateInstance(isServer ? ServerMessages.serverMessages[(MessageType)finalMessage[0]] : null, new object[] { finalMessage });
                                    }
                                    catch { }
                                    Console.WriteLine("{0}#{1,-3} {2,-30} {3}", isServer ? "S" : "C", finalMessage[0], messageObj?.GetType()?.Name ?? "Unknown", finalMessage.Length > 1 ? BitConverter.ToString(finalMessage, 1).Replace("-", "") : "");
                                }
                                if (isServer) serverIndex = 0;
                                else clientIndex = 0;
                                packetBytes--;
                            }
                        }
                    }
                }
                catch (SocketException e)
                {
                    Console.WriteLine("Connection to client lost");
                    Debug.WriteLine(e);
                }
            }
        }
    }
}
