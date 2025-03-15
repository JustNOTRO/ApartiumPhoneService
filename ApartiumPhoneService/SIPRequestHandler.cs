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
    
    private readonly ConcurrentDictionary<char, VoIpSound> _keySounds = new();

    private readonly List<char> _keysPressed = [];

    private readonly VoIpAudioPlayer _audioPlayer;
    
    private readonly SIPUserAgentFactory _userAgentFactory;
    
    private readonly ILogger _logger;
    
    public SIPRequestHandler(ApartiumPhoneServer server, SIPUserAgentFactory userAgentFactory, VoIpAudioPlayer audioPlayer, ILogger logger)
    {
        _server = server;
        _userAgentFactory = userAgentFactory;
        _audioPlayer = audioPlayer;
        _logger = logger;

        AddKeySounds();
    }

    /// <summary>
    /// Handles requests by method type
    /// </summary>
    public async Task Handle(SIPRequest sipRequest, SIPEndPoint sipEndPoint, SIPEndPoint remoteEndPoint)
    {
        var sipTransport = _server.GetSipTransport();

        switch (sipRequest.Method)
        {
            case SIPMethodsEnum.INVITE:
            {
                await HandleIncomingCall(sipTransport, sipRequest, sipEndPoint, remoteEndPoint);
                break;
            }

            case SIPMethodsEnum.BYE:
            {
                var byeResponse = SIPResponse.GetResponse(sipRequest,
                    SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                await sipTransport.SendResponseAsync(byeResponse);
                break;
            }

            case SIPMethodsEnum.SUBSCRIBE:
            {
                var notAllowedResponse =
                    SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                await sipTransport.SendResponseAsync(notAllowedResponse);
                break;
            }

            case SIPMethodsEnum.OPTIONS:
            case SIPMethodsEnum.REGISTER:
            {
                var optionsResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                await sipTransport.SendResponseAsync(optionsResponse);
                break;
            }
        }
    }

    private readonly object _threadLock = new();
    private readonly ManualResetEvent _manualReset = new(false);
    
    /// <summary>
    /// Handles incoming call
    /// </summary>
    /// <param name="sipTransport">the server SIP transport</param>
    private async Task HandleIncomingCall(SIPTransport sipTransport, SIPRequest sipRequest, SIPEndPoint sipEndPoint, SIPEndPoint remoteEndPoint)
    {
        _logger.LogInformation($"Incoming call request: {sipEndPoint}<-{remoteEndPoint} {sipRequest.URI}.");
        
        var sipUserAgent = _userAgentFactory.Create(sipTransport, null);
        sipUserAgent.OnCallHungup += OnHangup;

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
        
        var serverUserAgent = sipUserAgent.AcceptCall(sipRequest);

        if (serverUserAgent.IsCancelled)
        {
            _logger.LogInformation("Incoming call cancelled by remote party.");
            return;
        }
        
        await sipUserAgent.Answer(serverUserAgent, voIpMediaSession);
        
        var call = new SIPOngoingCall(sipUserAgent, serverUserAgent, _audioPlayer);
        if (!_server.TryAddCall(sipUserAgent.Dialogue().CallId, call))
        {
            _logger.LogWarning("Could not add call to active calls");
        }

        await Task.Run(() => _audioPlayer.Play(VoIpSound.WelcomeSound));
        _manualReset.Set(); // signaling the thread to continue.
    }

    /// <summary>
    /// Handles the call when client hangup
    /// </summary>
    /// <param name="dialogue">The call dialogue</param>
    private void OnHangup(SIPDialogue dialogue)
    {
        var callId = dialogue.CallId;
        var call = _server.TryRemoveCall(callId);

        // This app only uses each SIP user agent once so here the agent is 
        // explicitly closed to prevent is responding to any new SIP requests.
        call?.Hangup();
        _audioPlayer.Stop();
        _logger.LogInformation("Stopped audio");
    }

    private readonly object _keyLock = new();
    
    /// <summary>
    /// Handles DTMF tones received from client
    /// </summary>
    /// <param name="userAgent">The user agent of the client</param>
    /// <param name="key">The key that was pressed</param>
    /// <param name="duration">The duration of the press</param>
    private void OnDtmfTone(SIPUserAgent userAgent, byte key, int duration)
    {
        _manualReset.WaitOne(); // waiting until the welcome sound has finished
        
        var callId = userAgent.Dialogue.CallId;
        _logger.LogInformation($"Call {callId} received DTMF tone {key}, duration {duration}ms.");

        var keyPressed = key switch
        {
            10 => '*',
            11 => '#',
            _ => key.ToString()[0]
        };

        _logger.LogInformation("User pressed {0}!", keyPressed);
        
        if (keyPressed != '#')
        {
            AddKeyPressed(keyPressed);
            return;
        }
        
        lock (_threadLock)
        {
            PlaySelectedNumbers();
        }
    }

    /// <summary>
    /// Plays the selected numbers the client chose
    /// </summary>
    private void PlaySelectedNumbers()
    {
        foreach (var sound in _keysPressed.Select(GetVoIpSound))
        {
            _audioPlayer.Play(sound);
        }

        lock (_keyLock)
        {
            _keysPressed.Clear();
            _logger.LogInformation("Cleared the keys pressed");
        }
    }

    /// <summary>
    /// Adds the key pressed by client
    /// </summary>
    /// <param name="keyPressed">the key pressed</param>
    private void AddKeyPressed(char keyPressed)
    {
        lock (_keyLock)
        {
            _keysPressed.Add(keyPressed);
        }
    }
    
    /// <summary>
    /// Adds the key sounds from 0 to 9
    /// </summary>
    private void AddKeySounds()
    {
        var voIpSounds = VoIpSound.Values();

        for (var i = 1; i < voIpSounds.Length; i++)
        {
            var key = (i - 1).ToString()[0];
            _keySounds.TryAdd(key, voIpSounds[i]);
        }
    }

    private VoIpSound GetVoIpSound(char keyPressed)
    {
        return _keySounds[keyPressed];
    }
}