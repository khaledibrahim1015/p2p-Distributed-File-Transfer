using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace p2p2;

public class Peer
{
    private const int ChunkSize = 8192; // 8 KB chunks  1024* 8 
    private const string DefaultPath = "C:\\Users\\ZALL-TECH\\Desktop\\bittorrent\\Data2";
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private readonly ConcurrentDictionary<string, TcpClient> _connections = new();
    private readonly ConcurrentDictionary<string, FileTransferProgress> _transfers = new ConcurrentDictionary<string, FileTransferProgress>();
    private const int Port = 5000;
    public PeerRole Role { get; }

    public Peer(PeerRole role)
    {
        Role = role;
    }

    public async Task StartAsync(string otherPeerIp = null)
    {
        if (Role == PeerRole.Receiver)
        {
            await StartListenerAsync();

        }
        else if (Role == PeerRole.Sender && !string.IsNullOrEmpty(otherPeerIp))
        {
            await ConnectToPeerAsync(otherPeerIp);
        }


    }

    private async Task StartListenerAsync()
    {
        TcpListener listener = new TcpListener(IPAddress.Any, Port);
        listener.Start();
        Console.WriteLine($"Receiver Listening On Port {Port}");
        while (!_cts.IsCancellationRequested)
        {
            //  accept incoming requests 
            TcpClient client = await listener.AcceptTcpClientAsync();
            _ = HandleIncomingConnectionAsync(client);
        }
    }


    private async Task HandleIncomingConnectionAsync(TcpClient client)
    {
        using NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[ChunkSize]; // 1kb

        while (!_cts.IsCancellationRequested)
        {
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead == 0)
                break;

            string message = Encoding.UTF8.GetString(buffer, 0, bytesRead).TrimEnd('\0');  //  to ignore restof bytes that not fill 
            Console.WriteLine($"Received: {message}");

            if (message.StartsWith("FILE:"))
            {
                string metadate = message.Substring(5);
                await ReceiveFileAsync(stream, metadate);
            }
            else
            {
                // Handle other types of messages here
                Console.WriteLine($"Message received: {message}");
            }
        }
    }
    private async Task ReceiveFileAsync(NetworkStream stream, string metadataString)
    {
        try
        {
            FileMetaData metadata = ParseFileMetadata(metadataString);
            FileTransferProgress progress = new FileTransferProgress()
            {
                FileName = metadata.FileName,
                TotalBytes = metadata.FileSize
            };
            _transfers[metadata.FileName] = progress;
            // 2 - Receive file size
            //byte[] fileSizeBuffer = new byte[8];
            //int fileSizeBytesRead = await stream.ReadAsync(fileSizeBuffer, 0, fileSizeBuffer.Length);

            //if (fileSizeBytesRead != 8)
            //{
            //    Console.WriteLine("Failed to receive file size correctly.");
            //    return;
            //}

            //long fileSize = BitConverter.ToInt64(fileSizeBuffer, 0);

            // 2 - receive file meta data 



            Console.WriteLine($"Expected file size: {metadata.FileSize} bytes");

            // 3 - Receive file content
            string filePath = Path.Combine(DefaultPath, metadata.FileName);
            using FileStream fileStream = new FileStream(filePath, FileMode.Create);
            byte[] buffer = new byte[ChunkSize];
            int bytesRead;
            //long totalBytesReceived = 0;

            while (progress.BytesTransfered < metadata.FileSize)
            {
                bytesRead = await stream.ReadAsync(buffer, 0, Math.Min(buffer.Length, (int)(metadata.FileSize - progress.BytesTransfered)));
                if (bytesRead == 0) break; // Connection closed prematurely

                await fileStream.WriteAsync(buffer, 0, bytesRead);
                progress.BytesTransfered += bytesRead;
                //Console.WriteLine($"Receiving file: {totalBytesReceived * 100 / fileSize}% complete");
                ReportProgress(progress);
            }

            await fileStream.FlushAsync();

            if (progress.BytesTransfered != metadata.FileSize)
            {
                Console.WriteLine($"Warning: Received {progress.BytesTransfered} bytes, expected {metadata.FileSize} bytes.");
            }
            else
            {
                Console.WriteLine($"\nFile received: {metadata.FileName}");
                _transfers.TryRemove(metadata.FileName, out _);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error receiving file: {ex.Message}");
        }
    }

    public FileMetaData ParseFileMetadata(string metadataString)
    {
        var parts = metadataString.Split('|');
        return new FileMetaData()
        {
            FileName = parts[0],
            FileSize = long.Parse(parts[1])
        };



    }
    private async Task<bool> EnsureConnectedAsync()
    {
        if (!_connections.TryGetValue("receiver", out var client) || !client.Connected)
        {
            Console.WriteLine("Connection lost. Attempting to reconnect...");
            string ipAddress = "127.0.0.1"; // Todo: want to store this when initially connecting
            await ConnectToPeerAsync(ipAddress);
            return _connections.TryGetValue("receiver", out client) && client.Connected;
        }
        return true;
    }
    private async Task ConnectToPeerAsync(string ipAddress)
    {
        try
        {
            TcpClient client = new TcpClient();
            await client.ConnectAsync(ipAddress, Port);
            Console.WriteLine($"Connected To Receiver at Endpoint :{ipAddress}:{Port}");
            _connections["receiver"] = client;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to connect to receiver: {ex.Message}");
        }



    }

    public async Task SendMessageAsync(string message)
    {
        if (!await EnsureConnectedAsync())
        {
            Console.WriteLine("Failed to connect to receiver. Message not sent.");
            return;
        }

        if (!_connections.TryGetValue("receiver", out var client))
        {
            Console.WriteLine("Not connected to a receiver.");
            return; // Exit the method if there's no connection.
        }

        using NetworkStream stream = client.GetStream();
        byte[] buffer = Encoding.UTF8.GetBytes(message);
        await stream.WriteAsync(buffer, 0, buffer.Length);
        Console.WriteLine($"Message sent: {message}");
    }

    public async Task SendFileAsync(string filePath)
    {

        if (!await EnsureConnectedAsync())
        {
            Console.WriteLine("Failed to connect to receiver. File not sent.");
            return;
        }


        if (!_connections.TryGetValue("receiver", out var client))
        {
            Console.WriteLine("Not connected to a receiver.");
            return; // Exit the method if there's no connection.
        }

        using NetworkStream stream = client.GetStream();

        using FileStream fileStream = File.OpenRead(filePath);
        FileInfo fileInfo = new FileInfo(filePath);

        //  file metadata 
        FileMetaData metadata = new FileMetaData()
        {
            FileName = fileInfo.Name,
            FileSize = fileInfo.Length,
        };

        FileTransferProgress progress = new()
        {
            FileName = metadata.FileName,
            TotalBytes = metadata.FileSize,
        };
        _transfers[metadata.FileName] = progress;


        // 1 - Send metaData  (include filename and filesize )
        string metadataString = $"FILE:{metadata.FileName}|{metadata.FileSize}";
        byte[] metadataBytes = Encoding.UTF8.GetBytes(metadataString);
        await stream.WriteAsync(metadataBytes, 0, metadataBytes.Length);

        //// 2 - Send file size
        //long fileSize = fileInfo.Length;
        //byte[] fileSizeBytes = BitConverter.GetBytes(fileSize);
        //await stream.WriteAsync(fileSizeBytes, 0, fileSizeBytes.Length);

        // 2 -   // Send file content in chunks
        int bytesRead;
        //long totalBytesSent = 0;
        byte[] buffer = new byte[ChunkSize];

        while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await stream.WriteAsync(buffer, 0, bytesRead);
            //totalBytesSent += bytesRead;
            progress.BytesTransfered += bytesRead;
            ReportProgress(progress);
            //Console.WriteLine($"Sending file: {totalBytesSent * 100 / fileSize}% complete");
        }
        await stream.FlushAsync();
        Console.WriteLine($"\nFile sent: {filePath}");
        _transfers.TryRemove(metadata.FileName, out _);
    }

    private void ReportProgress(FileTransferProgress progress)
    {
        Console.WriteLine($"Transferring {progress.FileName}: {progress.ProgressPercentage:F2}% complete");
    }
    public void PrintActiveTransfers()
    {
        foreach (var transfer in _transfers)
        {
            Console.WriteLine($"{transfer.Key}: {transfer.Value.ProgressPercentage:F2}% complete");
        }
    }
    public void Stop()
    {

        _cts.Cancel();
    }
}
