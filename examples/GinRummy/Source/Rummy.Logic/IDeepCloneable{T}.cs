namespace Rummy.Logic
{
    public interface IDeepCloneable<out T>
    {
        T DeepClone();
    }
}
