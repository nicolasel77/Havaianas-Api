// Genera las claves VAPID para las notificaciones push: dotnet "Havaianas Api.dll" generar-claves-push
if (args.Length > 0 && args[0] == "generar-claves-push")
{
    var claves = WebPush.VapidHelper.GenerateVapidKeys();
    Console.WriteLine($"Push__ClavePublica={claves.PublicKey}");
    Console.WriteLine($"Push__ClavePrivada={claves.PrivateKey}");
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Configuración de Kestrel con puertos fijos
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5143); // HTTP
    options.ListenAnyIP(7254, listenOptions =>
    {
        listenOptions.UseHttps(); // HTTPS (requiere certificado)
    });
});

// Add CORS services
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        builder =>
        {
            builder.AllowAnyOrigin()
                   .AllowAnyMethod()
                   .AllowAnyHeader();
        });
});

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddSingleton<Havaianas_Api.Servicios.AvisosPush>();

// Swagger / OpenAPI (Swashbuckle)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowAll");

app.UseAuthorization();

app.MapControllers();

app.Run();