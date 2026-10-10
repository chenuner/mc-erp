using System.Security.Cryptography;
using System.Text;
using Isopoh.Cryptography.Argon2;

namespace MC.Erp.Common.Security;

public sealed class PasswordHasher : IPasswordHasher
{
    public string Hash(string password)
    {
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);

        var hash = Argon2.Hash(new Argon2Config
        {
            Type = Argon2Type.DataIndependentAddressing,
            Password = Encoding.UTF8.GetBytes(password),
            Salt = salt,
            TimeCost = 3,
            MemoryCost = 65536,
            Lanes = 4,
            HashLength = 32
        });

        return hash;
    }

    public bool Verify(string password, string hash)
    {
        return Argon2.Verify(hash, Encoding.UTF8.GetBytes(password));
    }
}
