using Havaianas_Api.Datos;
using Havaianas_Api.Modelos;
using Havaianas_Api.Servicios;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

// Comando para crear usuarios sin levantar la Api:
//   dotnet "Havaianas Api.dll" crear-usuario <usuario> <nombre> [idSucursal]
if (args.Length > 0 && args[0] == "crear-usuario")
{
    return CrearUsuario(args);
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
    throw new InvalidOperationException("Falta configurar Jwt:Clave (mínimo 32 caracteres). Ver README de la Api.");
}
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<TokenService>();

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
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "unique_name"
        };
    });

// Todos los endpoints piden token salvo los marcados con [AllowAnonymous] (ej: /Auth/login)
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
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

    Console.Write("Contraseña: ");
    var password = LeerOculto();
    Console.Write("Repetir contraseña: ");
    if (password != LeerOculto())
    {
        Console.WriteLine("Las contraseñas no coinciden.");
        return 1;
    }
    if (password.Length < 8)
    {
        Console.WriteLine("La contraseña debe tener al menos 8 caracteres.");
        return 1;
    }

    var usuario = new usuarios { usuario = args[1], nombre = args[2], id_sucursal = idSucursal };
    var hash = new PasswordHasher<usuarios>().HashPassword(usuario, password);
    new sql_db().crear_usuario(usuario.usuario, usuario.nombre, hash, idSucursal);

    Console.WriteLine($"Usuario '{usuario.usuario}' creado.");
    return 0;
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
