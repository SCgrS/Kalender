using System.Threading;

namespace Kalender.Core;

/// <summary>
/// Aynı anda yalnızca bir Kalender çalışsın diye adlandırılmış bir muteks tutar.
/// İki örnek aynı anda masaüstü katmanına yazarsa takvim titrer.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Kalender.TekOrnek";

    private Mutex? _mutex;

    public bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out bool created);
            if (created)
            {
                return true;
            }

            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        catch (Exception ex)
        {
            // Muteks kurulamadıysa engel çıkarmayıp çalışmaya devam ederiz.
            Log.Warn("Tek örnek denetimi kurulamadı.", ex);
            return true;
        }
    }

    public void Dispose()
    {
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Muteks bize ait değilse serbest bırakmaya çalışmak hata verir; önemsiz.
        }

        _mutex?.Dispose();
        _mutex = null;
    }
}
