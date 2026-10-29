# EduMonitor AI

Middleware docente con agente de IA para el seguimiento acadÃ©mico personalizado sobre Moodle.
Universidad TÃ©cnica de Ambato Â· FISEI Â· Carrera de Software Â· MetodologÃ­as Ãgiles Â· Cuarto "B".

Equipo: Vinicio Arias (Scrum Master / backend) Â· Damian Bastidas (Moodle / DevOps) Â· Mateo Morales (frontend) Â· Jostin Sigcha (analÃ­tica / IA).
Product Owner: Ing. HernÃ¡n Naranjo.

> El sistema **no califica, no modifica notas en Moodle** y no sustituye la valoraciÃ³n del docente.

## Estado: Sprint 1 Â· Fundaciones e integraciÃ³n con Moodle

| Historia | DescripciÃ³n | Estado en el cÃ³digo |
|---|---|---|
| HU-01 | Moodle propio con datos de prueba | `docker-compose.yml` + `moodle/*.csv` + `docs/GUIA_MOODLE.md` |
| HU-02 | Servicios web REST y token limitado | GuÃ­a T2.1 + colecciÃ³n Postman (carpeta Â«Moodle directoÂ») |
| HU-03 | 3 microservicios + Gateway + SQL Server + RabbitMQ | SoluciÃ³n .NET 8, Dockerfiles, `/swagger` y `/health` |
| HU-04 | Login del docente con roles | MS-A: BCrypt + JWT; Gateway: polÃ­ticas por rol; React: login y rutas protegidas |
| HU-05 | Materias asignadas desde Moodle | MS-B: `MoodleClient` + sincronizaciÃ³n; pantalla Â«Mis materiasÂ» (M2) |
| HU-06 | Estudiantes y actividades | MS-B: endpoints paginados con autorizaciÃ³n por curso; pantalla M3 |

## Arquitectura

```
React (SPA :5174) â”€â”€HTTPS/RESTâ”€â”€â–º API Gateway YARP (:5000)  â”€â”€ valida JWT y rol
                                     â”œâ”€â”€ /api/auth, /api/auditoria â”€â–º MS-A Identidad y AuditorÃ­a (:5001) â”€â–º DB_Identidad
                                     â”œâ”€â”€ /api/materias, /api/moodle â”€â–º MS-B AcadÃ©mico (:5002) â”€â–º DB_Academico
                                     â”‚                                     â””â”€â”€ API REST de Moodle (solo lectura) â”€â–º Moodle Docker (:8081)
                                     â””â”€â”€ /api/analitica, /api/agente â”€â–º MS-C AnalÃ­tica y Agente IA (:5003) â”€â–º DB_Analitica
                     RabbitMQ (:5672 / panel :15672) â€” eventos CalificacionesSincronizadas y MapeoActividadActualizado (Sprint 2)
```

```
EduMonitor.sln
src/
  BuildingBlocks/EduMonitor.Common   JWT, polÃ­ticas por rol y Swagger compartidos
  Gateway/EduMonitor.Gateway         YARP (rutas en appsettings.json)
  Services/Identity/...Identity.Api  MS-A Â· Usuario, Rol, RegistroAuditoria
  Services/Academic/...Academic.Api  MS-B Â· Materia, Estudiante, Matricula, Actividad, SincronizacionLog
  Services/Analytics/...Analytics.Api MS-C Â· esqueleto (Sprints 2 y 3)
tests/                               xUnit (login/token/rol, clasificaciÃ³n, permisos)
frontend/                            React + Vite
moodle/                              CSV de cursos, usuarios y notas ficticias
postman/                             ColecciÃ³n de pruebas del Sprint 1
docs/GUIA_MOODLE.md                  Paso a paso de Moodle y servicios web
```

## Puesta en marcha (un solo comando)

Requisitos: Docker Desktop (8 GB de RAM asignados recomendado), Git. Para desarrollo local: .NET 8 SDK y Node 20.

```bash
git clone https://github.com/<organizacion>/edumonitor-ai.git
cd edumonitor-ai
cp .env.example .env            # completar contraseÃ±as y, luego, MOODLE_TOKEN
docker compose up -d --build
```

| Servicio | URL |
|---|---|
| Frontend | http://localhost:5174 |
| API Gateway | http://localhost:5000/health |
| MS-A Swagger | http://localhost:5001/swagger |
| MS-B Swagger | http://localhost:5002/swagger |
| MS-C Swagger | http://localhost:5003/swagger |
| Moodle | http://localhost:8081 |
| RabbitMQ | http://localhost:15672 |

La primera vez configurar Moodle siguiendo **`docs/GUIA_MOODLE.md`** (cursos, 40/30/30, actividades, usuarios, notas y token). Luego pegar el token en `.env` y ejecutar `docker compose up -d academic-api`.

### Usuarios de prueba (EduMonitor)

| Correo | Rol | Uso |
|---|---|---|
| docente1@uta.edu.ec | Docente | DueÃ±o de PW-401-B y BD-402-B |
| docente2@uta.edu.ec | Docente | Sin materias: prueba de permisos |
| admin@edumonitor.local | AdministradorTecnico | No ve materias; auditorÃ­a (Sprint 3) |

ContraseÃ±a: el valor de `SEED_DEFAULT_PASSWORD` del `.env`.

## Desarrollo local sin Docker para el cÃ³digo

```bash
docker compose up -d sqlserver rabbitmq mariadb moodle
# Backend (en terminales separadas); exportar Jwt__Key, ConnectionStrings__Default, Moodle__Token...
dotnet run --project src/Services/Identity/EduMonitor.Identity.Api --urls http://localhost:5001
dotnet run --project src/Services/Academic/EduMonitor.Academic.Api --urls http://localhost:5002
dotnet run --project src/Services/Analytics/EduMonitor.Analytics.Api --urls http://localhost:5003
dotnet run --project src/Gateway/EduMonitor.Gateway --urls http://localhost:5000
# Frontend
cd frontend && npm install && npm run dev
```

## Pruebas

```bash
dotnet test EduMonitor.sln
```

- `EduMonitor.Identity.Tests`: login correcto/incorrecto, token con rol y expiraciÃ³n, validaciÃ³n del formulario (T4.6).
- `EduMonitor.Academic.Tests`: clasificaciÃ³n de actividades y pesos 40/30/30; un docente no accede a la materia de otro (T6.5).
- Postman: `postman/EduMonitor.postman_collection.json` (salud, login, materias, permisos y Moodle directo).

## Flujo de trabajo (GitFlow)

- `main`: versiÃ³n estable que se demuestra en la Sprint Review.
- `develop`: integraciÃ³n. Todo entra por **Pull Request revisado por otro integrante**.
- `feature/HU-xx-descripcion`: una rama por historia o tarea. Ej.: `feature/HU-04-login`.

```bash
git checkout develop && git pull
git checkout -b feature/HU-05-mis-materias
git commit -m "feat(HU-05): sincronizar materias del docente desde Moodle"
git push -u origin feature/HU-05-mis-materias   # abrir PR hacia develop
```

ConvenciÃ³n de commits: `feat`, `fix`, `test`, `docs`, `chore` + `(HU-xx)`.

## Notas tÃ©cnicas

- **Base de datos:** en el Sprint 1 cada servicio crea su base con `EnsureCreated()`. Cuando el modelo se estabilice:
  `dotnet ef migrations add Inicial --project src/Services/Academic/EduMonitor.Academic.Api` y reemplazar por `Database.Migrate()`.
- **Secretos:** solo en `.env` (no versionado). El token de Moodle y la clave JWT nunca se escriben en el cÃ³digo.
- **Moodle de solo lectura (RN-06):** el token solo tiene funciones de consulta.
- **ClasificaciÃ³n de actividades:** en el Sprint 1 se hace por el nombre de la actividad (`ActivityClassifier`). En el Sprint 2 se contrasta con las categorÃ­as del libro de calificaciones.
