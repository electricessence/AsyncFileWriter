using System.Threading.Tasks;

namespace Open
{
	public interface IDisposableAsync
	{
		Task DisposeAsync();
	}
}