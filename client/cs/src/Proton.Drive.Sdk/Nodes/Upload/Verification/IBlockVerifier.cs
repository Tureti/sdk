namespace Proton.Drive.Sdk.Nodes.Upload.Verification;

public interface IBlockVerifier
{
    VerificationToken VerifyBlock(ReadOnlyMemory<byte> dataPacketPrefix, ReadOnlySpan<byte> plainDataPrefix);
}
