namespace Proton.Drive.Sdk.Nodes.Upload;

public sealed class SmallUploadNotApplicableException : Exception
{
    public SmallUploadNotApplicableException(string message)
        : base(message)
    {
    }

    public SmallUploadNotApplicableException()
    {
    }

    public SmallUploadNotApplicableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
