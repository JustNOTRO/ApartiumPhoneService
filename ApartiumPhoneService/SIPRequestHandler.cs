using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace ApartiumPhoneService;

/// <summary>
/// <c>SIPRequestHandler</c> Handles a SIP request
/// </summary>
/// <param name="server">the SIP server</param>
/// <param name="sipRequest">the SIP request</param>
/// <param name="sipEndPoint">the SIP endpoint</param>
/// <param name="remoteEndPoint">the remote end point</param>
public class SIPRequestHandler
{
    private readonly ApartiumPhoneServer _server;

    private readonly SIPRequest _sipRequest;

    private readonly SIPEndPoint _sipEndPoint;

    private readonly SIPEndPoint _remoteEndPoint;

    private readonly ConcurrentDictionary<char, VoIpSound> _keySounds = new();

    private readonly ConcurrentBag<char> _keysPressed = [];

    private readonly VoIpAudioPlayer _voIpAudioPlayer;
    
    private ISIPUserAgentFactory _userAgentFactory;
    
    private readonly Microsoft.Extensions.Logging.ILogger _logger;
    
    public SIPRequestHandler(ApartiumPhoneServer server, SIPRequest sipRequest, SIPEndPoint sipEndPoint,
        SIPEndPoint remoteEndPoint, ILogger logger)
    {
        _server = server;
        _logger = logger;
        _sipRequest = sipRequest;
        _sipEndPoint = sipEndPoint;
        _remoteEndPoint = remoteEndPoint;
        _voIpAudioPlayer = new VoIpAudioPlayer();
        _userAgentFactory = new SIPUserAgentFactory();
        
        AddKeySounds();
    }

    /// <summary>
    /// Handles requests by method type
    /// </summary>
    public async Task Handle()
    {
        var sipTransport = _server.GetSipTransport();

        switch (_sipRequest.Method)
        {
            case SIPMethodsEnum.INVITE:
            {
                await HandleIncomingCall(sipTransport);
                break;
            }

            case SIPMethodsEnum.BYE:
            {
                var byeResponse = SIPResponse.GetResponse(_sipRequest,
                    SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                await sipTransport.SendResponseAsync(byeResponse);
                break;
            }

            case SIPMethodsEnum.SUBSCRIBE:
            {
                var notAllowedResponse =
                    SIPResponse.GetResponse(_sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                await sipTransport.SendResponseAsync(notAllowedResponse);
                break;
            }

            case SIPMethodsEnum.OPTIONS:
            case SIPMethodsEnum.REGISTER:
            {
                var optionsResponse = SIPResponse.GetResponse(_sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                await sipTransport.SendResponseAsync(optionsResponse);
                break;
            }
        }
    }

    /// <summary>
    /// Handles incoming call
    /// </summary>
    /// <param name="sipTransport">the server SIP transport</param>
    private async Task HandleIncomingCall(SIPTransport sipTransport)
    {
        _logger.LogInformation($"Incoming call request: {_sipEndPoint}<-{_remoteEndPoint} {_sipRequest.URI}.");

        
        var sipUserAgent = _userAgentFactory.Create(sipTransport, null);
        sipUserAgent.OnCallHungup += OnHangup;
        sipUserAgent.ServerCallCancelled += (_, _) => _logger.LogDebug("Incoming call cancelled by remote party.");

        var audioSource = new AudioExtrasSource();
        var voIpMediaSession = new VoIPMediaSession(new MediaEndPoints { AudioSource = audioSource });

        sipUserAgent.OnDtmfTone += (key, duration) => OnDtmfTone(sipUserAgent, key, duration);
        sipUserAgent.OnRtpEvent += (evt, hdr) =>
            _logger.LogDebug(
                $"rtp event {evt.EventID}, duration {evt.Duration}, end of event {evt.EndOfEvent}, timestamp {hdr.Timestamp}, marker {hdr.MarkerBit}.");

        //ua.OnTransactionTraceMessage += (tx, msg) => Log.LogDebug($"uas tx {tx.TransactionId}: {msg}");
        sipUserAgent.ServerCallRingTimeout += serverUserAgent =>
        {
            var transactionState = serverUserAgent.ClientTransaction.TransactionState;
            _logger.LogWarning(
                $"Incoming call timed out in {transactionState} state waiting for client ACK, terminating.");
            sipUserAgent.Hangup();
        };
        
        var serverUserAgent = sipUserAgent.AcceptCall(_sipRequest);

        await sipUserAgent.Answer(serverUserAgent, voIpMediaSession);

        if (!sipUserAgent.IsCallActive)
        {
            _logger.LogWarning("Call is not active");
            return;
        }
        
        var call = new SIPOngoingCall(sipUserAgent, serverUserAgent, _voIpAudioPlayer);
        if (!_server.TryAddCall(sipUserAgent.Dialogue.CallId, call))
        {
            _logger.LogWarning("Could not add call to active calls");
        }

        _voIpAudioPlayer.Play(VoIpSound.WelcomeSound);
    }

    /// <summary>
    /// Handles the call when client hangup
    /// </summary>
    /// <param name="dialogue">The call dialogue</param>
    private void OnHangup(SIPDialogue? dialogue)
    {
        if (dialogue == null)
        {
            return;
        }

        var callId = dialogue.CallId;
        var call = _server.TryRemoveCall(callId);

        // This app only uses each SIP user agent once so here the agent is 
        // explicitly closed to prevent is responding to any new SIP requests.
        call?.Hangup();
    }

    /// <summary>
    /// Handles DTMF tones received from client
    /// </summary>
    /// <param name="userAgent">The user agent of the client</param>
    /// <param name="key">The key that was pressed</param>
    /// <param name="duration">The duration of the press</param>
    private void OnDtmfTone(SIPUserAgent userAgent, byte key, int duration)
    {
        var callId = userAgent.Dialogue.CallId;
        _logger.LogInformation($"Call {callId} received DTMF tone {key}, duration {duration}ms.");

        var keyPressed = key switch
        {
            10 => '*',
            11 => '#',
            _ => key.ToString()[0]
        };

        Console.WriteLine("User pressed {0}!", keyPressed);

        if (keyPressed != '#')
        {
            _keysPressed.Add(keyPressed);
            return;
        }
        
        PlaySelectedNumbers(userAgent).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Plays the selected numbers the client chose
    /// </summary>
    private async Task PlaySelectedNumbers(SIPUserAgent userAgent)
    {
        foreach (var sound in _keysPressed.Select(GetVoIpSound))
        {
            _voIpAudioPlayer.Play(sound);
            await Task.Delay(sound.GetDuration() * 1000);
        }

        _keysPressed.Clear();
        Console.WriteLine("Cleared the keys pressed");
    }

    /// <summary>
    /// Adds the key sounds
    /// </summary>
    private void AddKeySounds()
    {
        _keySounds.TryAdd('0', VoIpSound.ZeroSound);
        _keySounds.TryAdd('1', VoIpSound.OneSound);
        _keySounds.TryAdd('2', VoIpSound.TwoSound);
        _keySounds.TryAdd('3', VoIpSound.ThreeSound);
        _keySounds.TryAdd('4', VoIpSound.FourSound);
        _keySounds.TryAdd('5', VoIpSound.FiveSound);
        _keySounds.TryAdd('6', VoIpSound.SixSound);
        _keySounds.TryAdd('7', VoIpSound.SevenSound);
        _keySounds.TryAdd('8', VoIpSound.EightSound);
        _keySounds.TryAdd('9', VoIpSound.NineSound);
    }

    private VoIpSound GetVoIpSound(char keyPressed)
    {
        return _keySounds[keyPressed];
    }
    
    /// <summary>
    /// Initializes the sip request handler logger
    /// </summary>
    /// <returns>the server logger</returns>
    private Microsoft.Extensions.Logging.ILogger InitLogger()
    {
        var serilogLogger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
            .WriteTo.Console()
            .CreateLogger();

        var factory = new SerilogLoggerFactory(serilogLogger);
        SIPSorcery.LogFactory.Set(factory);
        return factory.CreateLogger<SIPRequestHandler>();
    }
}