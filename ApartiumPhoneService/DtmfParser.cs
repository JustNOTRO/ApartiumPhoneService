namespace ApartiumPhoneService;

public class DtmfParser
{
    private readonly Dictionary<byte, char> _keys = new()
    {
        { 0x01, '1' },
        { 0x02, '2' },
        { 0x03, '3' },
        { 0x04, '4' },
        { 0x05, '5' },
        { 0x06, '6' },
        { 0x07, '7' },
        { 0x08, '8' },
        { 0x09, '9' },
        { 0x0A, '0' },
        { 0x0B, '*' },
        { 0x0C, '#' },
        { 0x0D, 'A' },
        { 0x0E, 'B' },
        { 0x0F, 'C' },
        { 0x10, 'D' }
    };

    public char Parse(byte key)
    {
        try
        {
            return _keys[key];
        }
        catch (Exception _)
        {
            throw new ApplicationException("Invalid key: " + key);
        }
    }

    public byte Parse(char key)
    {
        foreach (var entry in _keys.Where(keyValuePair => keyValuePair.Value == key))
        {
            return entry.Key;
        }

        throw new KeyNotFoundException("Invalid Dtmf tone.");
    }
}