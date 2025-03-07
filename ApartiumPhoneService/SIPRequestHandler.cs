using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

namespace ApartiumPhoneService;

/// <summary>
/// <c>SIPRequestHandler</c> Handles a SIP request
/// </summary>
/// <param name="server">the SIP server</param>
/// <param name="sipRequest">the SIP request</param>
/// <param name="sipEndPoint">the SIP endpoint</param>
/// <param name="remoteEndPoint">the remote end point</param>
public class SIPRequestHandler {

    private readonly ApartiumPhoneServer _server;
    
    private readonly SIPRequest _sipRequest;
    
    private readonly SIPEndPoint _sipEndPoint;
    
    private readonly SIPEndPoint _remoteEndPoint;
    
    private readonly ConcurrentDictionary<char, VoIpSound> _keySounds = new();
    
    public SIPRequestHandler(ApartiumPhoneServer server, SIPRequest sipRequest, SIPEndPoint sipEndPoint,
        SIPEndPoint remoteEndPoint)
    {
        _server = server;
        _sipRequest = sipRequest;
        _sipEndPoint = sipEndPoint;
        _remoteEndPoint = remoteEndPoint;

        AddKeySounds();
    }
    
    private readonly List<char> _keysPressed = [];
    
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
        var logger = ApartiumPhoneServer.GetLogger();
        logger.LogInformation($"Incoming call request: {_sipEndPoint}<-{_remoteEndPoint} {_sipRequest.URI}.");

        var sipUserAgent = new SIPUserAgent(sipTransport, null);
        sipUserAgent.OnCallHungup += OnHangup;
        sipUserAgent.ServerCallCancelled += (_, _) => logger.LogDebug("Incoming call cancelled by remote party.");

        var mediaSession = new VoIPMediaSession();
        
        sipUserAgent.OnDtmfTone += (key, duration) => OnDtmfTone(mediaSession, sipUserAgent, key, duration);
        sipUserAgent.OnRtpEvent += (evt, hdr) =>
            logger.LogDebug(
                $"rtp event {evt.EventID}, duration {evt.Duration}, end of event {evt.EndOfEvent}, timestamp {hdr.Timestamp}, marker {hdr.MarkerBit}.");

        //ua.OnTransactionTraceMessage += (tx, msg) => Log.LogDebug($"uas tx {tx.TransactionId}: {msg}");
        sipUserAgent.ServerCallRingTimeout += serverUserAgent =>
        {
            var transactionState = serverUserAgent.ClientTransaction.TransactionState;
            logger.LogWarning(
                $"Incoming call timed out in {transactionState} state waiting for client ACK, terminating.");
            sipUserAgent.Hangup();
        };

        var serverUserAgent = sipUserAgent.AcceptCall(_sipRequest);
        
        await sipUserAgent.Answer(serverUserAgent, mediaSession);
        
        if (!sipUserAgent.IsCallActive)
        {
            return;
        }
        
        var call = new SIPOngoingCall(sipUserAgent, serverUserAgent);
        if (!_server.TryAddCall(sipUserAgent.Dialogue.CallId, call))
        {
            logger.LogWarning("Could not add call to active calls");
        }

        await PlaySound(mediaSession, sipUserAgent, VoIpSound.WelcomeSound);
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
    /// <param name="session">the VoIP media session</param>
    /// <param name="userAgent">The user agent of the client</param>
    /// <param name="key">The key that was pressed</param>
    /// <param name="duration">The duration of the press</param>
    private async Task OnDtmfTone(VoIPMediaSession session, SIPUserAgent userAgent, byte key, int duration)
    {
        var logger = ApartiumPhoneServer.GetLogger();
        var callId = userAgent.Dialogue.CallId;
        logger.LogInformation($"Call {callId} received DTMF tone {key}, duration {duration}ms.");

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
        
        foreach (var sound in _keysPressed.Select(GetVoIpSound))
        {
            await PlaySound(session, userAgent, sound);
        }
            
        _keysPressed.Clear();
    }
    
    /// <summary>
    /// Plays a sound
    /// </summary>
    /// <param name="session">The VoIP media session</param>
    /// <param name="userAgent">the client user agent</param>
    /// <param name="destination">the destination of the sound</param>
    /// <param name="duration">the sound duration (in seconds)</param>
    private async Task PlaySound(VoIPMediaSession session, SIPUserAgent userAgent, VoIpSound sound)
    {
        var logger = ApartiumPhoneServer.GetLogger();
        session.AcceptRtpFromAny = true;

        session.OnTimeout += _ =>
        {
            logger.LogWarning(userAgent.Dialogue != null
                ? $"RTP timeout on call with {userAgent.Dialogue.RemoteTarget}, hanging up."
                : $"RTP timeout on incomplete call, closing RTP session.");

            userAgent.Hangup();
        };

        await session.AudioExtrasSource.StartAudio();
        
        var audioStream = new FileStream(sound.Destination, FileMode.Open);
        await session.AudioExtrasSource.SendAudioFromStream(audioStream, AudioSamplingRatesEnum.Rate8KHz);
        await Task.Delay(sound.Duration * 1000);
        
        await session.Start();
    }

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
    
}