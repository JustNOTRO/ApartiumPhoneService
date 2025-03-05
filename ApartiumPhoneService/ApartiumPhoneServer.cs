using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

namespace ApartiumPhoneService;

public class ApartiumPhoneServer
{
    private const int SipListenPort = 5060;
    private const string LocalHostAddress = "127.0.0.1";
    private const string LocalHostIpV4Address = "127.0.0.1";
    private const string LocalHostIpV6Address = "::1";
    private const string LocalHostIpv4Cmd = "lv4";
    private const string LocalHostIpv6Cmd = "lv6";

    private readonly ConcurrentDictionary<string, SIPUserAgent> _calls = new();

    private readonly ConfigDataProvider _configDataProvider;

    private readonly SIPTransport _sipTransport;

    private static readonly Microsoft.Extensions.Logging.ILogger Logger = InitLogger();

    private IPAddress _address;

    public ApartiumPhoneServer(string serverFilePath)
    {
        _sipTransport = new SIPTransport();
        _configDataProvider = new ConfigDataProvider(serverFilePath);
    }

    public static Microsoft.Extensions.Logging.ILogger GetLogger()
    {
        return Logger;
    }

    public SIPTransport GetSipTransport()
    {
        return _sipTransport;
    }

    private bool ValidateIPAddress(string? ip)
    {
        if (string.IsNullOrEmpty(ip))
        {
            Console.WriteLine();
            Console.WriteLine("IP Address cannot be empty.");
            return false;
        }
        
        ip = ip.ToLower() switch
        {
            LocalHostIpv4Cmd => LocalHostAddress,
            LocalHostIpv6Cmd => LocalHostIpV6Address,
            _ => ip
        };

        IPAddress address = null;
        try
        {
            address = IPAddress.Parse(ip);
        }
        catch (Exception _)
        {
            Console.WriteLine();
            Console.WriteLine($"Invalid IP Address: '{ip}'");
            return false;
        }

        _address = address;
        return true;
    }
    
    public void Start()
    {
        Console.WriteLine("-----Lazy commands-----");
        Console.WriteLine("{0} - equivalent to {1} (IPV4 localhost)", LocalHostIpv4Cmd, LocalHostIpV4Address);
        Console.WriteLine("{0} - equivalent to {1} (IPV6 localhost)", LocalHostIpv6Cmd, LocalHostIpV6Address);
        Console.WriteLine();
        
        Console.Write("Please enter IP Address: ");
        var ipAddress = Console.ReadLine();
        if (!ValidateIPAddress(ipAddress))
        {
            return;
        }

        _sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(_address, SipListenPort)));
        _sipTransport.EnableTraceLogs();
        StartRegistrations();
        _sipTransport.SIPTransportRequestReceived += OnRequest;

        var localhostType = _address.ToString().Equals(LocalHostIpV4Address) ? "(IPV4 localhost)" :
            _address.ToString().Equals(LocalHostIpV6Address) ? "(IPV6 localhost)" : "";

        Console.WriteLine();
        Console.WriteLine("Started ApartiumPhoneServer!");
        Console.WriteLine("Server is running on IP: {0} {1}", _address.ToString(), localhostType);
        
        Console.WriteLine("Press any key to shutdown...");
        
        // Ensuring it is not a test case
        if (Console.LargestWindowWidth != 0)
        {
            Console.ReadKey(true);
            _sipTransport.Shutdown();
            _calls.Clear();
        }
        
        Logger.LogInformation("Exiting...");
        Logger.LogInformation("Shutting down SIP transport...");
    }

    private async Task OnRequest(SIPEndPoint localSipEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
    {
        if (sipRequest.Header.From.FromTag != null && sipRequest.Header.To.ToTag != null)
        {
            return;
        }
        
        var sipRequestHandler = new SIPRequestHandler(this, sipRequest, localSipEndPoint, remoteEndPoint);
        await sipRequestHandler.Handle();
    }

    /// <summary>
    /// Starts a registration agent for each of the supplied SIP accounts.
    /// </summary>
    private void StartRegistrations()
    {
        var sipAccounts = _configDataProvider.GetRegisteredAccounts();

        foreach (var sipAccount in sipAccounts)
        {
            var userName = sipAccount.Username;
            var domain = sipAccount.Domain;

            var regUserAgent = new SIPRegistrationUserAgent(_sipTransport, userName, sipAccount.Password,
                domain, sipAccount.Expiry);

            // Event handlers for the different stages of the registration.
            regUserAgent.RegistrationFailed += (uri, _, err) => Logger.LogError($"{uri}: {err}");
            regUserAgent.RegistrationTemporaryFailure += (uri, _, msg) => Logger.LogWarning($"{uri}: {msg}");
            regUserAgent.RegistrationRemoved += (uri, _) => Logger.LogError($"{uri} registration failed.");
            regUserAgent.RegistrationSuccessful += (uri, _) =>
                Logger.LogInformation($"{uri} registration succeeded.");

            regUserAgent.UserDisplayName = userName;

            // Start the thread to perform the initial registration and then periodically resend it.
            regUserAgent.Start();
        }
    }
    
    public void TryAddCall(string key, SIPUserAgent userAgent)
    {
        _calls.TryAdd(key, userAgent);
    }

    public SIPUserAgent? TryRemoveCall(string callId)
    {
        _calls.TryRemove(callId, out var userAgent);
        return userAgent;
    }

    private static Microsoft.Extensions.Logging.ILogger InitLogger()
    {
        var serilogLogger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
            .WriteTo.Console()
            .CreateLogger();

        var factory = new SerilogLoggerFactory(serilogLogger);
        SIPSorcery.LogFactory.Set(factory);
        return factory.CreateLogger<ApartiumPhoneServer>();
    }
}