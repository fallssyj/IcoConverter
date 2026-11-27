namespace IcoConverter.Services
{
    public readonly record struct BatchConvertResult(int Total, int Success, int Failed);
}