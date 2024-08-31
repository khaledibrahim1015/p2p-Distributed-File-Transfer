
using System.Net;
using System.Net.Sockets;

namespace p2p
{
    public class Program
    {
        private const string ReceivePath = "C:\\Users\\ZALL-TECH\\Desktop\\bittorrent\\Data\\received_file.txt";
        static async Task Main(string[] args)
        {
            Console.WriteLine("P2P File Transfer");
            Console.WriteLine("1. Receive File");
            Console.WriteLine("2. Send File");
            Console.Write("Choose an option: ");

            string choice = Console.ReadLine();
            switch (choice)
            {
                case "1":
                    await ReceiveFileAsync();
                    return;
                case "2":
                    await SendFileAsync();
                    return;
                default:
                    Console.WriteLine("not exist ");
                    break;



            }
        }



        private static async Task ReceiveFileAsync()
        {
            Console.Write("Enter Port To Listen On:");
            int port = int.Parse(Console.ReadLine());

            TcpListener tcpListener = new TcpListener(IPAddress.Any, port);
            tcpListener.Start();
            Console.WriteLine($"Listening on Port :{port}");

            using TcpClient client = await tcpListener.AcceptTcpClientAsync();
            Console.WriteLine($"Client Sender  connected on {client.Client.RemoteEndPoint}");

            //  open stream 
            using NetworkStream networkStream = client.GetStream();
            using FileStream fs = new FileStream(ReceivePath, FileMode.Create);
            //  create buffer to read in
            byte[] buffer = new byte[1024];
            int bytesRead;
            while ((bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fs.WriteAsync(buffer, 0, bytesRead);
            }

            Console.WriteLine("File received successfully.");


        }


        private static async Task SendFileAsync()
        {
            Console.Write("Enter File Path: ");
            string filePath = Console.ReadLine();
            Console.Write("Enter receiver's IP address: ");
            string ipaddress = Console.ReadLine();
            Console.Write("Enter Port Number : ");
            int port = int.Parse(Console.ReadLine());

            using TcpClient tcpClient = new TcpClient();
            //IPEndPoint iPEndPoint = new IPEndPoint(ipaddress, port);
            await tcpClient.ConnectAsync(ipaddress, port);
            Console.WriteLine($"Connected to receiver {tcpClient.Client.RemoteEndPoint}");

            using NetworkStream networkStream = tcpClient.GetStream();
            using FileStream fs = File.OpenRead(filePath);

            byte[] buffer = new byte[1024];
            int bytesRead;
            while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                //  write on stream 
                await networkStream.WriteAsync(buffer, 0, bytesRead);

            }

            Console.WriteLine("File sent successfully.");
        }
    }
}
