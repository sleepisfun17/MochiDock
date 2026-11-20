using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using MochiDock.Models;

namespace MochiDock.Services;

public class FileShelfService
{
    private static readonly string DropFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MochiDock", "PairedDrop");

    public ObservableCollection<ShelfFileItem> ShelfFiles { get; } = new();

    private readonly PhoneDropServer _server = new(DropFolder);
    public int Port => 47824;
    public string LocalIpAddress { get; set; } = "";
    public string DropUrl => _server.Url;
    public bool IsRunning => _server.IsRunning;
    public string Status => _server.Status;
    public bool HasPendingApproval => _server.HasPendingApproval;
    public event Action? Changed;
    /// <summary>A file arrived from a phone (not one added by hand).</summary>
    public event Action? PhoneFileReceived;
    public event Action<DropProposal>? ApprovalRequested;
    public static string[] GetAddresses() => System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
            n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback &&
            n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Tunnel)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
        .Select(a => a.Address.ToString()).Distinct().ToArray();
    public FileShelfService()
    {
        LocalIpAddress = GetAddresses().FirstOrDefault() ?? "";
        _server.Changed += () => Changed?.Invoke();
        _server.ApprovalRequested += proposal => ApprovalRequested?.Invoke(proposal);
        _server.FileReceived += path => App.Current.Dispatcher.BeginInvoke(() => { AddFile(path); PhoneFileReceived?.Invoke(); });
    }
    public Task StartServerAsync()
    {
        if (!GetAddresses().Contains(LocalIpAddress)) throw new InvalidOperationException("Select an available LAN IPv4 address.");
        return _server.StartAsync(IPAddress.Parse(LocalIpAddress), Port);
    }
    public Task StopServerAsync() => _server.StopAsync();
    public bool Answer(string id, bool approved) => _server.Answer(id, approved);
    public void AddFile(string path)
    {
        if (!File.Exists(path)) return;
        var fi = new FileInfo(path);

        // Avoid duplicate paths
        foreach (var item in ShelfFiles)
        {
            if (item.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)) return;
        }

        ShelfFiles.Insert(0, new ShelfFileItem
        {
            FilePath = path,
            FileSizeBytes = fi.Length
        });
    }

    public void RemoveFile(ShelfFileItem item)
    {
        ShelfFiles.Remove(item);
    }

    public void ClearShelf()
    {
        ShelfFiles.Clear();
    }

}
