namespace Havaianas_Api.Modelos
{
    // Mismo formato que PushSubscription.toJSON() del navegador
    public class SuscripcionPush
    {
        public string endpoint { get; set; } = string.Empty;
        public Claves keys { get; set; } = new Claves();

        public class Claves
        {
            public string p256dh { get; set; } = string.Empty;
            public string auth { get; set; } = string.Empty;
        }
    }
}
