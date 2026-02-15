using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class CueOscReceiver : MonoBehaviour
{
    [Header("Cue Target")]
    [SerializeField] private CueController cueController;
    [SerializeField] private bool autoFindCueController = true;

    [Header("OSC Input")]
    [SerializeField] private int listenPort = 9000;
    [SerializeField] private string runCueAddress = "/cue/run";
    [SerializeField] private string jumpCueAddress = "/cue/jump";
    [SerializeField] private bool verboseLogging = false;

    private readonly Queue<QueuedCommand> commandQueue = new Queue<QueuedCommand>();
    private readonly object queueLock = new object();

    private UdpClient udpClient;
    private bool isListening;

    private enum CueOscCommandType
    {
        Run,
        JumpAndRun
    }

    private struct QueuedCommand
    {
        public CueOscCommandType Type;
        public int CueIndex;
        public string Sender;
        public string Address;
    }

    private void Awake()
    {
        TryAssignCueController();
    }

    private void OnEnable()
    {
        StartListening();
    }

    private void OnDisable()
    {
        StopListening();
    }

    private void Update()
    {
        ProcessQueuedCommands();
    }

    private void TryAssignCueController()
    {
        if (cueController != null || !autoFindCueController)
            return;

        cueController = GetComponent<CueController>();
        if (cueController != null)
            return;

#if UNITY_2023_1_OR_NEWER
        cueController = FindFirstObjectByType<CueController>();
#else
        cueController = FindObjectOfType<CueController>();
#endif
    }

    private void StartListening()
    {
        if (isListening)
            return;

        TryAssignCueController();

        try
        {
            udpClient = new UdpClient(listenPort);
            isListening = true;
            BeginReceive();
            Debug.Log($"CueOscReceiver listening on UDP {listenPort}");
        }
        catch (Exception ex)
        {
            isListening = false;
            udpClient = null;
            Debug.LogError($"CueOscReceiver failed to start on port {listenPort}: {ex.Message}");
        }
    }

    private void StopListening()
    {
        isListening = false;
        if (udpClient == null)
            return;

        try
        {
            udpClient.Close();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"CueOscReceiver socket close failed: {ex.Message}");
        }

        udpClient = null;
    }

    private void BeginReceive()
    {
        if (!isListening || udpClient == null)
            return;

        try
        {
            udpClient.BeginReceive(OnUdpPacketReceived, null);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"CueOscReceiver begin receive failed: {ex.Message}");
        }
    }

    private void OnUdpPacketReceived(IAsyncResult asyncResult)
    {
        if (!isListening || udpClient == null)
            return;

        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        byte[] packet = null;

        try
        {
            packet = udpClient.EndReceive(asyncResult, ref remote);
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"CueOscReceiver receive failed: {ex.Message}");
        }
        finally
        {
            BeginReceive();
        }

        if (packet == null || packet.Length == 0)
            return;

        ParseOscPacket(packet, remote);
    }

    private void ParseOscPacket(byte[] packet, IPEndPoint remote)
    {
        if (OscReader.IsBundle(packet, packet.Length))
        {
            if (!OscReader.TryParseBundle(packet, packet.Length, out List<OscMessage> bundleMessages))
                return;

            for (int i = 0; i < bundleMessages.Count; i++)
                TryQueueOscMessage(bundleMessages[i], remote);

            return;
        }

        if (OscReader.TryParseMessage(packet, 0, packet.Length, out OscMessage message))
            TryQueueOscMessage(message, remote);
    }

    private void TryQueueOscMessage(OscMessage message, IPEndPoint remote)
    {
        string normalizedAddress = NormalizeAddress(message.Address);
        if (string.IsNullOrEmpty(normalizedAddress))
            return;

        bool isRun = IsAddressMatchOrChild(normalizedAddress, runCueAddress);
        bool isJump = IsAddressMatchOrChild(normalizedAddress, jumpCueAddress);

        if (!isRun && !isJump)
        {
            if (verboseLogging)
                Debug.Log($"CueOscReceiver ignored OSC address {message.Address}");
            return;
        }

        if (!TryResolveCueIndex(normalizedAddress, message.Arguments, out int cueIndex))
        {
            Debug.LogWarning($"CueOscReceiver could not resolve cue index from OSC address {message.Address}");
            return;
        }

        var command = new QueuedCommand
        {
            Type = isJump ? CueOscCommandType.JumpAndRun : CueOscCommandType.Run,
            CueIndex = cueIndex,
            Sender = remote != null ? remote.ToString() : "unknown",
            Address = message.Address
        };

        lock (queueLock)
        {
            commandQueue.Enqueue(command);
        }
    }

    private void ProcessQueuedCommands()
    {
        while (true)
        {
            QueuedCommand command;

            lock (queueLock)
            {
                if (commandQueue.Count == 0)
                    return;

                command = commandQueue.Dequeue();
            }

            ExecuteCommand(command);
        }
    }

    private void ExecuteCommand(QueuedCommand command)
    {
        if (cueController == null)
        {
            TryAssignCueController();
            if (cueController == null)
            {
                Debug.LogWarning("CueOscReceiver got OSC command but no CueController is assigned.");
                return;
            }
        }

        switch (command.Type)
        {
            case CueOscCommandType.Run:
                cueController.TryTriggerCue(command.CueIndex);
                break;
            case CueOscCommandType.JumpAndRun:
                cueController.JumpToCueAndRun(command.CueIndex);
                break;
        }

        if (verboseLogging)
            Debug.Log($"CueOscReceiver {command.Type} cue {command.CueIndex} from {command.Sender} ({command.Address})");
    }

    private static bool IsAddressMatchOrChild(string incomingAddress, string configuredAddress)
    {
        string normalizedConfigured = NormalizeAddress(configuredAddress);
        if (string.IsNullOrEmpty(normalizedConfigured))
            return false;

        if (incomingAddress == normalizedConfigured)
            return true;

        return incomingAddress.StartsWith(normalizedConfigured + "/", StringComparison.Ordinal);
    }

    private static string NormalizeAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return string.Empty;

        string normalized = address.Trim();
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = "/" + normalized;

        return normalized.ToLowerInvariant();
    }

    private static bool TryResolveCueIndex(string address, List<object> arguments, out int cueIndex)
    {
        cueIndex = -1;

        if (arguments != null && arguments.Count > 0 && TryConvertToCueIndex(arguments[0], out cueIndex))
            return true;

        int lastSlash = address.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash + 1 < address.Length)
        {
            string lastSegment = address.Substring(lastSlash + 1);
            if (int.TryParse(lastSegment, NumberStyles.Integer, CultureInfo.InvariantCulture, out cueIndex))
                return true;
        }

        return false;
    }

    private static bool TryConvertToCueIndex(object value, out int cueIndex)
    {
        cueIndex = -1;
        switch (value)
        {
            case int intValue:
                cueIndex = intValue;
                return true;
            case long longValue:
                cueIndex = (int)longValue;
                return true;
            case float floatValue:
                cueIndex = Mathf.RoundToInt(floatValue);
                return true;
            case double doubleValue:
                cueIndex = (int)Math.Round(doubleValue);
                return true;
            case string stringValue:
                return int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out cueIndex);
            default:
                return false;
        }
    }

    private readonly struct OscMessage
    {
        public readonly string Address;
        public readonly List<object> Arguments;

        public OscMessage(string address, List<object> arguments)
        {
            Address = address;
            Arguments = arguments;
        }
    }

    private static class OscReader
    {
        public static bool IsBundle(byte[] data, int length)
        {
            return length >= 8
                && data[0] == (byte)'#'
                && data[1] == (byte)'b'
                && data[2] == (byte)'u'
                && data[3] == (byte)'n'
                && data[4] == (byte)'d'
                && data[5] == (byte)'l'
                && data[6] == (byte)'e'
                && data[7] == 0;
        }

        public static bool TryParseBundle(byte[] data, int length, out List<OscMessage> messages)
        {
            messages = new List<OscMessage>();
            if (!IsBundle(data, length) || length < 16)
                return false;

            int offset = 16; // "#bundle\0" + 8-byte timetag
            while (offset + 4 <= length)
            {
                if (!TryReadInt32(data, ref offset, length, out int elementSize))
                    return false;

                if (elementSize <= 0 || offset + elementSize > length)
                    return false;

                if (TryParseMessage(data, offset, elementSize, out OscMessage message))
                    messages.Add(message);

                offset += elementSize;
            }

            return messages.Count > 0;
        }

        public static bool TryParseMessage(byte[] data, int start, int length, out OscMessage message)
        {
            message = default;

            int offset = start;
            int end = start + length;

            if (!TryReadPaddedString(data, ref offset, end, out string address))
                return false;

            if (!TryReadPaddedString(data, ref offset, end, out string typeTag))
                return false;

            if (string.IsNullOrEmpty(typeTag) || typeTag[0] != ',')
                return false;

            var arguments = new List<object>(Mathf.Max(0, typeTag.Length - 1));
            for (int i = 1; i < typeTag.Length; i++)
            {
                switch (typeTag[i])
                {
                    case 'i':
                        if (!TryReadInt32(data, ref offset, end, out int intValue))
                            return false;
                        arguments.Add(intValue);
                        break;
                    case 'h':
                        if (!TryReadInt64(data, ref offset, end, out long longValue))
                            return false;
                        arguments.Add(longValue);
                        break;
                    case 'f':
                        if (!TryReadFloat32(data, ref offset, end, out float floatValue))
                            return false;
                        arguments.Add(floatValue);
                        break;
                    case 'd':
                        if (!TryReadFloat64(data, ref offset, end, out double doubleValue))
                            return false;
                        arguments.Add(doubleValue);
                        break;
                    case 's':
                        if (!TryReadPaddedString(data, ref offset, end, out string stringValue))
                            return false;
                        arguments.Add(stringValue);
                        break;
                    case 'T':
                        arguments.Add(true);
                        break;
                    case 'F':
                        arguments.Add(false);
                        break;
                    default:
                        return false;
                }
            }

            message = new OscMessage(address, arguments);
            return true;
        }

        private static bool TryReadPaddedString(byte[] data, ref int offset, int end, out string value)
        {
            value = string.Empty;
            if (offset >= end)
                return false;

            int start = offset;
            while (offset < end && data[offset] != 0)
                offset++;

            if (offset >= end)
                return false;

            int length = offset - start;
            value = Encoding.UTF8.GetString(data, start, length);

            offset++; // null terminator
            while (offset % 4 != 0)
            {
                if (offset >= end)
                    return false;
                offset++;
            }

            return true;
        }

        private static bool TryReadInt32(byte[] data, ref int offset, int end, out int value)
        {
            value = 0;
            if (offset + 4 > end)
                return false;

            value = (data[offset] << 24)
                | (data[offset + 1] << 16)
                | (data[offset + 2] << 8)
                | data[offset + 3];

            offset += 4;
            return true;
        }

        private static bool TryReadInt64(byte[] data, ref int offset, int end, out long value)
        {
            value = 0L;
            if (offset + 8 > end)
                return false;

            uint upper = (uint)(
                (data[offset] << 24)
                | (data[offset + 1] << 16)
                | (data[offset + 2] << 8)
                | data[offset + 3]);

            uint lower = (uint)(
                (data[offset + 4] << 24)
                | (data[offset + 5] << 16)
                | (data[offset + 6] << 8)
                | data[offset + 7]);

            value = ((long)upper << 32) | lower;
            offset += 8;
            return true;
        }

        private static bool TryReadFloat32(byte[] data, ref int offset, int end, out float value)
        {
            value = 0f;
            if (offset + 4 > end)
                return false;

            byte b0 = data[offset];
            byte b1 = data[offset + 1];
            byte b2 = data[offset + 2];
            byte b3 = data[offset + 3];
            offset += 4;

            byte[] orderedBytes = BitConverter.IsLittleEndian
                ? new[] { b3, b2, b1, b0 }
                : new[] { b0, b1, b2, b3 };

            value = BitConverter.ToSingle(orderedBytes, 0);
            return true;
        }

        private static bool TryReadFloat64(byte[] data, ref int offset, int end, out double value)
        {
            value = 0d;
            if (offset + 8 > end)
                return false;

            byte[] orderedBytes;
            if (BitConverter.IsLittleEndian)
            {
                orderedBytes = new[]
                {
                    data[offset + 7], data[offset + 6], data[offset + 5], data[offset + 4],
                    data[offset + 3], data[offset + 2], data[offset + 1], data[offset]
                };
            }
            else
            {
                orderedBytes = new[]
                {
                    data[offset], data[offset + 1], data[offset + 2], data[offset + 3],
                    data[offset + 4], data[offset + 5], data[offset + 6], data[offset + 7]
                };
            }

            offset += 8;
            value = BitConverter.ToDouble(orderedBytes, 0);
            return true;
        }
    }
}
