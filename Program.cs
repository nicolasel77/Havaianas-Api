using Havaianas_Api.Controllers;
using Havaianas_Api.Datos;
using Havaianas_Api.Modelos;
using Havaianas_Api.Servicios;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Security.Cryptography;

// Comandos que se corren en el servidor sin levantar la Api:
//   dotnet "Havaianas Api.dll" generar-claves-push
//   dotnet "Havaianas Api.dll" generar-clave                      (clave al azar para Jwt:Clave o ClavesSucursales)
//   dotnet "Havaianas Api.dll" crear-usuario <usuario> <nombre> [idSucursal]
//   dotnet "Havaianas Api.dll" resetear-password <usuario>        (si alguien se olvidó la contraseña)
if (args.Length > 0)
{
    switch (args[0])
    {
        case "generar-claves-push":
            var claves = WebPush.VapidHelper.GenerateVapidKeys();
            Console.WriteLine($"Push__ClavePublica={claves.PublicKey}");
            Console.WriteLine($"Push__ClavePrivada={claves.PrivateKey}");
            return 0;
        case "generar-clave":
            Console.WriteLine(Convert.ToBase64String(RandomNumberGenerator.GetBytes(36)).Replace('+', '-').Replace('/', '_'));
            return 0;
        case "crear-usuario":
            return CrearUsuario(args);
        case "resetear-password":
            return ResetearPassword(args);
    }
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

// JWT: la clave NO va en el repo. En el servidor se carga con la variable de entorno Jwt__Clave
// (o en appsettings.Production.json, que está en .gitignore).
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOpciones>() ?? new JwtOpciones();
if (jwt.Clave.Length < 32)
{
    throw new InvalidOperationException("Falta configurar Jwt:Clave (mínimo 32 caracteres). Generala con: dotnet \"Havaianas Api.dll\" generar-clave");
}
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<LimitadorLogin>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ValidadorSesiones>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // deja los claims con su nombre original (sub, unique_name, ...)
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Emisor,
            ValidateAudience = true,
            ValidAudience = jwt.Audiencia,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwt.ObtenerClave(),
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "unique_name"
        };
        options.Events = new JwtBearerEvents
        {
            // Token bien firmado pero de un usuario dado de baja o que cambió la contraseña
            OnTokenValidated = context =>
            {
                var sesiones = context.HttpContext.RequestServices.GetRequiredService<ValidadorSesiones>();
                if (context.Principal == null || !sesiones.EsValido(context.Principal))
                {
                    context.Fail("La sesión ya no es válida.");
                }
                return Task.CompletedTask;
            }
        };
    })
    // Programas de las sucursales (Havaianas Escritorio): cabecera X-Clave-Sucursal
    .AddScheme<AuthenticationSchemeOptions, ClaveSucursalHandler>(ClaveSucursalHandler.Esquema, null);

// Todos los endpoints piden token (o clave de sucursal) salvo los marcados con [AllowAnonymous] (ej: /Auth/login)
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme, ClaveSucursalHandler.Esquema)
        .RequireAuthenticatedUser()
        .Build();
});

// CORS: solo los orígenes de la WebApp configurados en Cors:Origenes
var origenes = builder.Configuration.GetSection("Cors:Origenes").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("WebApp",
        builder =>
        {
            builder.WithOrigins(origenes)
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
builder.Services.AddSwaggerGen(options =>
{
    // Botón "Authorize" en Swagger para pegar el token
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Token obtenido en POST /Auth/login"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseCors("WebApp");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
return 0;

static int CrearUsuario(string[] args)
{
    if (args.Length < 3)
    {
        Console.WriteLine("Uso: crear-usuario <usuario> <nombre> [idSucursal]");
        return 1;
    }

    int? idSucursal = args.Length > 3 ? int.Parse(args[3]) : null;
    var usuario = new usuarios { usuario = args[1].Trim(), nombre = args[2], id_sucursal = idSucursal };

    var password = PedirPasswordNueva(usuario.usuario);
    if (password == null) return 1;

    var hash = new PasswordHasher<usuarios>().HashPassword(usuario, password);
    new sql_db().crear_usuario(usuario.usuario, usuario.nombre, hash, idSucursal);

    Console.WriteLine($"Usuario '{usuario.usuario}' creado.");
    return 0;
}

static int ResetearPassword(string[] args)
{
    if (args.Length < 2)
    {
        Console.WriteLine("Uso: resetear-password <usuario>");
        return 1;
    }

    var sql = new sql_db();
    var usuario = sql.obtener_usuario(args[1].Trim());
    if (usuario == null)
    {
        Console.WriteLine($"No existe el usuario '{args[1]}'.");
        return 1;
    }

    var password = PedirPasswordNueva(usuario.usuario);
    if (password == null) return 1;

    // Cambia también el sello: se cierran todas las sesiones abiertas de ese usuario
    sql.cambiar_password_usuario(usuario.id, new PasswordHasher<usuarios>().HashPassword(usuario, password));
    Console.WriteLine($"Contraseña de '{usuario.usuario}' cambiada. Sus sesiones abiertas se cerraron.");
    return 0;
}

static string? PedirPasswordNueva(string usuario)
{
    Console.Write("Contraseña: ");
    var password = LeerOculto();
    Console.Write("Repetir contraseña: ");
    if (password != LeerOculto())
    {
        Console.WriteLine("Las contraseñas no coinciden.");
        return null;
    }
    var problema = AuthController.ProblemaPassword(password, usuario);
    if (problema != null)
    {
        Console.WriteLine(problema);
        return null;
    }
    return password;
}

static string LeerOculto()
{
    if (Console.IsInputRedirected)
    {
        return Console.ReadLine() ?? "";
    }

    var texto = new System.Text.StringBuilder();
    while (true)
    {
        var tecla = Console.ReadKey(intercept: true);
        if (tecla.Key == ConsoleKey.Enter) break;
        if (tecla.Key == ConsoleKey.Backspace)
        {
            if (texto.Length > 0) texto.Length--;
            continue;
        }
        texto.Append(tecla.KeyChar);
    }
    Console.WriteLine();
    return texto.ToString();
}
