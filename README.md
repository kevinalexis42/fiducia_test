# CORE-FID · Microservicio de Auditoría

Extracción del módulo de Auditoría del monolito CORE-FID (.NET Framework 4.8 / Oracle) a un
microservicio .NET 8 con Clean Architecture, CQRS y Outbox. Primera pieza del Strangler Fig.

Respeta **exactamente** el contrato vigente en producción ([`contrato/auditoria-legacy-v1.yaml`](contrato/auditoria-legacy-v1.yaml)):
mismas rutas, mismo envoltorio `{codigo, mensaje, detalle, data}`, mismos códigos `E01`–`E05`, `E10`, `E99`.

Las decisiones de diseño, los defectos encontrados en el legacy y el plan de corte están en [`DECISIONES.md`](DECISIONES.md).
El recorrido completo de verificación (15 pasos con lo que se espera en cada uno) está en [`GUIA-VERIFICACION.md`](GUIA-VERIFICACION.md).

## Alcance entregado

| Prioridad | Estado |
|---|---|
| **P0** Solución en 4 capas, dominio con invariantes, CQRS, repositorio + migración, endpoints + health + Swagger, Outbox transaccional, pruebas, `DECISIONES.md` | Completo |
| **P1** Dispatcher del Outbox (RabbitMQ o log), JWT, Dockerfile multi-stage + compose, logs JSON con `traceId` | Completo |
| **P2** Manifiestos K8s + `HTTPRoute` canary | Manifiestos incluidos en `deploy/k8s` (no aplicados a un cluster) |
| **P2** OpenTelemetry | Solo documentado (`DECISIONES.md` §5) |

## Levantar todo en un comando

```bash
docker compose up -d --build
```

Levanta PostgreSQL, RabbitMQ y el API. El API aplica la migración al arrancar y queda en `http://localhost:8080`.

| Recurso | URL |
|---|---|
| Swagger | http://localhost:8080/swagger |
| Liveness | http://localhost:8080/healthz |
| Readiness (verifica PostgreSQL) | http://localhost:8080/readyz |
| RabbitMQ Management | http://localhost:15672 (guest / guest) |

## Probar los endpoints

Los endpoints requieren un JWT HS256 con los valores de `starter/JWT-DEV.md`. Puedes generarlo en
https://jwt.io con el secreto `eval-secret-please-change-me-0123456789abcdef` y este payload:

```json
{
  "iss": "https://sts.windows.net/eval-tenant/",
  "aud": "api://corefid-audit",
  "sub": "b3f1a2c4-1111-2222-3333-444455556666",
  "preferred_username": "jperez",
  "roles": ["audit.write", "audit.read"],
  "exp": 1893456000
}
```

```bash
TOKEN="<pegar aquí>"

curl -s -X POST http://localhost:8080/api/auditoria/registro \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"codigoEmpresa":"0001","codigoModulo":"06","codigoTransaccion":"V062014","usuario":"jperez","entidad":"SolicitudRescate","claveEntidad":"RES-2026-004871","accion":"U","valorAnterior":"{\"estado\":\"PENDIENTE\"}","valorNuevo":"{\"estado\":\"APROBADA\"}","direccionIp":"10.0.4.77","canal":"API"}'

curl -s "http://localhost:8080/api/auditoria/consulta?codigoEmpresa=0001&usuario=jperez" \
  -H "Authorization: Bearer $TOKEN"
```

Tras registrar, en pocos segundos el dispatcher publica el evento `AuditoriaRegistrada` en el exchange
`corefid.auditoria` de RabbitMQ y marca `processed_at` en `outbox_messages`. Si detienes RabbitMQ
(`docker stop eval-rabbitmq`) el API sigue respondiendo y los eventos quedan pendientes con backoff;
al volver el broker se publican solos.

## Ejecutar las pruebas

```bash
dotnet test
```

- **Unitarias** (`tests/Unit`): invariantes del dominio y handler del command con reloj y repositorio falsos.
- **Integración** (`tests/Integration`): `WebApplicationFactory` + PostgreSQL efímero con Testcontainers.
  Verifican el shape del contrato, la atomicidad auditoría+outbox, el dispatcher con broker caído y la validación JWT.
  Requieren Docker en ejecución.

## Estructura

```
src/
  Domain/           RegistroAuditoria (agregado append-only), value objects Accion/Canal/CodigoEmpresa/CodigoModulo,
                    evento AuditoriaRegistrada, DomainException con los códigos del contrato. Sin dependencias.
  Application/      Puertos.cs (ICommandHandler/IQueryHandler, IAuditoriaRepository, IUnitOfWork, IEventPublisher),
                    RegistrarAuditoria (command + handler), ConsultarAuditoria (query + handler), DTO.
  Infrastructure/   AuditoriaDbContext (mapeo EF + migración), UnitOfWork (auditoría + outbox en una transacción),
                    AuditoriaRepository, AuditoriaReadModel, OutboxDispatcher, publishers RabbitMQ y log.
  Api/              AuditoriaController (thin), DTOs del contrato, Seguridad.cs (JWT HS256/JWKS + políticas),
                    ManejoErroresMiddleware (E99 sin stack trace, E00 en modelo inválido), Program.cs.
tests/
  Unit/             Invariantes del dominio y handler con reloj y repositorio falsos.
  Integration/      WebApplicationFactory + Testcontainers: contrato, atomicidad, dispatcher, JWT, health.
deploy/
  sql/              Script idempotente del esquema (generado desde la migración EF).
  k8s/              Deployment, Service, PDB, ConfigMap/Secret y HTTPRoute con canary ponderado.
contrato/           Copia del contrato OpenAPI legacy que este servicio debe respetar.
```

## Configuración

Toda la configuración sensible entra por variables de entorno (`ConnectionStrings__Auditoria`, `Jwt__SymmetricKey`
o `Jwt__Authority`, `RabbitMq__*`). `appsettings.json` no contiene credenciales; `appsettings.Development.json`
y `docker-compose.yml` usan únicamente los valores de desarrollo publicados en el enunciado.

| Clave | Uso |
|---|---|
| `Jwt:SymmetricKey` | HS256 para desarrollo (si está presente, se usa) |
| `Jwt:Authority` | JWKS de Entra ID (RS256) para producción |
| `RabbitMq:Habilitado` | `false` → los eventos se publican al log (`LoggingEventPublisher`) |
| `Outbox:*` | Intervalo, tamaño de lote y backoff del dispatcher |
| `Consulta:MaximoResultados` | `0` = sin límite (comportamiento legacy); ver DECISIONES.md |
| `Database:MigrarAlIniciar` | `true` solo en local; en K8s el esquema se aplica con `deploy/sql` |
