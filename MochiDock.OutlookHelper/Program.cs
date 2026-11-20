using System.IO.Pipes;
using System.Text;
using MochiDock.Core;

namespace MochiDock.OutlookHelper;

/// <summary>
/// Runs at the user's normal privilege level so it can reach classic Outlook over COM, and relays new mail to MochiDock
/// (which runs elevated) through a private named pipe. Usage: MochiDock.OutlookHelper.exe &lt;pipeName&gt; &lt;secret&gt;
/// </summary>
static class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 2) return 2;
        string pipeName = args[0], secret = args[1];

        try { return Run(pipeName, secret); }
        catch { return 1; }
    }

    static int Run(string pipeName, string secret)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        pipe.Connect(15000);

        var utf8 = new UTF8Encoding(false);
        using var reader = new StreamReader(pipe, utf8);
        using var writer = new StreamWriter(pipe, utf8) { AutoFlush = true };
        var writeGate = new object();
        void Send(OutlookPipeMessage message)
        {
            lock (writeGate)
            {
                try { writer.WriteLine(message.Encode()); } catch (IOException) { }
            }
        }

        Send(OutlookPipeMessage.Hello(secret));

        var outlook = new OutlookComReader(text => Send(OutlookPipeMessage.Log(text)));
        outlook.MailArrived += mail => Send(OutlookPipeMessage.MailMessage(mail));

        // MochiDock sends the tracked folder names right after the handshake; start watching once they are known.
        var first = OutlookPipeMessage.TryDecode(reader.ReadLine());
        if (first?.T == "config") outlook.SetTrackedFolders(OutlookFolderPolicy.Split(first.Msg), first.OnlyListed == true);
        else if (first?.T == "stop") return 0;
        outlook.Start();

        try
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var message = OutlookPipeMessage.TryDecode(line);
                if (message is null) continue;
                switch (message.T)
                {
                    case "config": outlook.SetTrackedFolders(OutlookFolderPolicy.Split(message.Msg), message.OnlyListed == true); break;
                    case "open": outlook.Open(message.Id); break;
                    case "read": outlook.MarkRead(message.Id); break;
                    case "stop": return 0;
                }
            }
        }
        finally { outlook.Stop(); }
        return 0; // pipe closed: MochiDock exited
    }
}
