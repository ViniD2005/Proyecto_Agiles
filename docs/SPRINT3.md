# Sprint 3 · Agente de IA, recomendaciones, reportes y auditoría

**Objetivo:** el docente conversa con un agente que consulta datos reales de su curso, recibe recomendaciones por estudiante que él edita y aprueba, descarga un reporte en PDF y el administrador audita todo lo que ocurre.
**Alcance obligatorio:** 6 historias (HU-13 a HU-18) · 24 SP · 24 tareas · 90 h.
**Alcance opcional (reserva):** 3 historias (HU-19, HU-20, HU-21) · 11 SP · 6 tareas · 23 h. **Se implementaron las tres.**

Este paquete contiene **solo lo del Sprint 3** (archivos nuevos y los modificados). Se copia sobre el repositorio del Sprint 2 (tag `sprint-2`).

---

## 1. Qué se entrega, por historia

| Historia | SP | Qué hace | Dónde está |
|---|---|---|---|
| **HU-13** Agente con herramientas controladas | 5 | Consulta en lenguaje natural. El agente solo puede usar **6 herramientas**; el código (no el modelo) decide cuáles y con qué parámetros. Cada consulta usa ≥ 4 herramientas reales. Si no hay datos, lo dice y **no inventa** | `Analytics/Agente/*` |
| **HU-14** Respuesta con hechos, inferencias e incertidumbre | 3 | La respuesta separa **Hechos** (calculados por código), **Inferencias** y **Recomendaciones** (del modelo) e **Incertidumbre** (reglas). Pantalla M9 con el detalle de herramientas usadas | `AgenteOrquestador.cs`, `pages/AgenteIA.jsx` |
| **HU-15** Recomendaciones por estudiante | 5 | Generadas según el nivel (Crítico / En umbral / Aprobado) con la **evidencia** que arma el código. El docente las **edita y aprueba**; al editar vuelven a borrador | `Analytics/Recomendaciones/*`, `components/PanelRecomendaciones.jsx` |
| **HU-16** Reporte inteligente del curso | 3 | Vista previa y **PDF** con indicadores, hallazgos, temas críticos, recomendaciones grupales y aviso de «orientativo». La vista previa y el PDF salen del **mismo contenido guardado** | `Analytics/Reportes/*`, `pages/ReporteCurso.jsx` |
| **HU-17** Auditoría centralizada | 3 | Bitácora única en MS-A con filtros (acción, origen, usuario, fechas), **sin tokens ni contraseñas**. Solo la lee el administrador técnico | `Common/Auditoria/*`, `Identity/Services/AuditoriaService.cs`, `pages/Auditoria.jsx` |
| **HU-18** Documentación y cierre | 5 | README, `.env.example`, este documento, colección Postman, matriz RF/RNF, manual de usuario, escenarios del agente, pruebas | `README.md`, `docs/SPRINT3.md`, `postman/` |
| Gestión | — | Informe del sprint | TS3.1 |

### Historias opcionales (reserva) — implementadas

| Historia | SP | Qué hace | Dónde está |
|---|---|---|---|
| **HU-19** Tendencia y proyección | 3 | Regresión lineal sobre las notas evaluadas; proyecta la **siguiente nota** y lanza una **alerta temprana**. Necesita ≥ 3 notas; siempre se rotula como estimación | `Services/CalculadoraTendencia.cs`, ficha del estudiante |
| **HU-20** Envío por correo | 3 | Envía por SMTP la recomendación **aprobada**, solo cuando el docente presiona «Enviar». Nunca automático. El correo se pide a MS-B en ese instante (no se copia a la BD de analítica) | `Recomendaciones/EnvioCorreo.cs`, `Academic/Controllers/ContactoController.cs` |
| **HU-21** Sílabo en PDF | 5 | Sube el PDF → propone unidades, temas y contenidos (con el modelo, o por patrones si no hay modelo). **No guarda nada** hasta que el docente revisa y confirma con la importación del Sprint 2 | `Analytics/Silabo/*`, `components/SilaboPropuesta.jsx` |

---

## 2. Cómo funciona el agente (para defender)

```
Docente ── pregunta ──► AgenteController ─► AgenteOrquestador
                                              1. valida la materia (403 si no es suya, queda en auditoría)
                                              2. Anonimizador: nombres → E01, E02…  (RN-11)
                                              3. PlanificadorConsulta (REGLAS, no modelo): elige herramientas
                                              4. ejecuta herramientas → datos calculados por código (RN-09)
                                              5. HECHOS e INCERTIDUMBRE: salen del código
                                              6. ILlmProvider (Ollama | Gemini | Groq): solo redacta inferencias y recomendaciones
                                              7. descarta lo que cite un código inexistente; si no hay modelo → respuesta por reglas
                                              8. E01 → nombre real solo al devolver al docente
```

**Las 6 herramientas** (`listar_cursos_autorizados`, `obtener_calificaciones`, `identificar_alertas`, `generar_resumen`, `obtener_desempeno_temas`, `obtener_ficha_estudiante`) validan sus parámetros, solo leen y rechazan cualquier materia que no sea del docente. Cada invocación (herramienta, parámetros, resultado, milisegundos) se guarda en `InvocacionesHerramienta`.

### Garantías de diseño

| Garantía | Cómo se cumple |
|---|---|
| El modelo no inventa cifras | Los hechos y la incertidumbre los escribe el código; el modelo recibe cifras ya calculadas y su salida se filtra |
| El modelo no ve nombres (RN-11) | Marcadores `{est:ID}` → alias `E01…` antes de enviar; también se anonimiza la pregunta del docente |
| Un docente solo ve lo suyo (RN-07) | Validación en el orquestador y en cada herramienta; intento ajeno → 403 + `AGENTE_ACCESO_DENEGADO` |
| El modelo no elige acciones | El planificador es de reglas; el modelo no llama herramientas ni cambia la materia |
| Funciona sin modelo | Si Ollama no responde, se entrega la respuesta por reglas con una advertencia visible |
| Cero ≠ sin entrega ≠ pendiente (RN-08) | Se reutilizan los cálculos del Sprint 2 |
| Orientativo (RN-10) | Aviso en respuestas, recomendaciones, reportes y correos |

### T13.1 · Selección del modelo

| Criterio | `qwen2.5:3b` (por defecto) | Alternativas |
|---|---|---|
| Ejecución | Local con Ollama, gratis, sin enviar datos fuera | `llama3.2:3b`, `gemma2:2b`, `phi3.5` también corren en Ollama |
| Tamaño | ≈ 2 GB en disco; ≈ 4 GB de RAM con contexto 4096 | Similar en los de 2–4 B |
| Español y salida JSON | Buen español y respeta bien el formato JSON | Varía; `gemma2:2b` es más ligero pero menos estable en JSON |
| Cambio de modelo | `Llm__Model` en `.env`, sin tocar código | — |
| Proveedor en la nube (opcional) | `Llm__Provider=Gemini` o `Groq` con clave gratuita en `LLM_API_KEY` | Los datos siguen anonimizados |

> La elección se confirma probando en el equipo del grupo: si el modelo responde lento, se cambia `LLM_MODEL` o se usa Gemini/Groq. Nada más cambia.

---

## 3. Contenido del paquete

### Archivos **modificados** (⚠ revisar con `git diff` antes de aceptar)

| Archivo | Cambio |
|---|---|
| `docker-compose.yml` | Servicios `ollama` (perfil `ia`) y `mailpit` (perfil `correo`); variables `Llm__`, `Smtp__`, `Audit__IdentityUrl`, `Academic__BaseUrl`; MS-B y MS-C esperan a MS-A |
| `.env.example` | Variables nuevas `LLM_*`, `SMTP_*`, `REPORTE_PERIODO` |
| `src/Gateway/EduMonitor.Gateway/appsettings.json` | Rutas nuevas (`recomendaciones`, `silabo`, `reportes`); `/api/auditoria` pasa a **solo GET** y **solo administrador** |
| `…/Identity.Api/Controllers/AuditoriaController.cs`, `Program.cs` | Filtros y paginación; registro del servicio |
| `…/Academic.Api/Controllers/MateriasController.cs`, `EstructuraController.cs`, `Program.cs`, `appsettings.json` | Atributo de auditoría y cliente de auditoría |
| `…/Analytics.Api/EduMonitor.Analytics.Api.csproj` | +`QuestPDF` 2024.7.3, `MailKit` (4.x), `PdfPig` (el paquete se llama `PdfPig`; los espacios de nombres siguen siendo `UglyToad.PdfPig`) |
| `…/Analytics.Api/Program.cs`, `appsettings.json`, `Dockerfile` | Registro de servicios; configuración; fuentes para el PDF |
| `…/Analytics.Api/Data/AnalyticsDbContext.cs` | +4 tablas |
| `…/Analytics.Api/Contracts/Sprint2Dtos.cs`, `Services/AnaliticaService.cs` | La ficha incluye la tendencia (HU-19) |
| `frontend/src/App.jsx`, `api/client.js` | Rutas reales; soporte de archivos (multipart) y descarga de PDF |
| `frontend/src/pages/AdminInicio.jsx`, `EstructuraTematica.jsx`, `FichaEstudiante.jsx` | Enlace a auditoría; carga de sílabo; tendencia |

> Si ya editaron alguno, **no lo sobrescriban a ciegas**: comparen con `git diff` y fusionen a mano.

Todo lo demás es **nuevo**: `Common/Auditoria/`, `Analytics/Agente/`, `Recomendaciones/`, `Reportes/`, `Silabo/`, controladores, pruebas, pantallas (`AgenteIA`, `ReporteCurso`, `Auditoria`), `styles-sprint3.css`, `scripts/agregar-variables-sprint3.ps1`, `postman/EduMonitor.Sprint3.postman_collection.json` y este documento.

### Base de datos (solo cambia **DB_Analitica**; Identity y Academic no cambian de esquema)

| Tabla | Para qué | Columnas principales |
|---|---|---|
| `ConsultasAgente` | Una fila por consulta | Fecha, DocenteEmail, MateriaId, Pregunta (anonimizada), Intencion, Modelo, ModeloDisponible, DuracionMs, Resultado |
| `InvocacionesHerramienta` | Herramientas usadas por consulta | ConsultaId, Herramienta, Parametros, Exito, Resumen, DuracionMs |
| `Recomendaciones` | Una por (materia, estudiante) — índice único | Nivel, NotaPonderada, Evidencia, Contenido (JSON), Fuente, Estado, Generada/Editada/Aprobada/Enviada (fecha y quién) |
| `ReportesCurso` | Reportes guardados | MateriaId, GeneradoUtc, GeneradoPor, Contenido (JSON; de aquí salen la vista previa y el PDF) |

---

## 4. Endpoints nuevos (todos por el Gateway :5000)

| Método y ruta | Rol | Qué hace |
|---|---|---|
| `GET /api/agente/estado` | Docente | Proveedor, modelo, disponibilidad y herramientas |
| `POST /api/agente/chat` | Docente | `{materiaId, pregunta}` → hechos, inferencias, recomendaciones, incertidumbre, herramientas |
| `GET /api/materias/{id}/recomendaciones` | Docente | Estudiantes y estado de su recomendación |
| `GET /api/materias/{id}/recomendaciones/{estId}` | Docente | Detalle (404 si aún no existe) |
| `POST …/{estId}/generar` | Docente | Genera; 409 si ya está aprobada |
| `PUT …/{estId}` | Docente | Edita; vuelve a borrador |
| `POST …/{estId}/aprobar` | Docente | Aprueba |
| `POST …/{estId}/enviar` | Docente | Envía por correo (409 si no está aprobada; 503 si no hay SMTP) |
| `POST /api/materias/{id}/reporte` · `GET …/reporte` | Docente | Genera / consulta el último |
| `GET /api/reportes/{reporteId}/pdf` | Docente | Descarga el PDF |
| `POST /api/materias/{id}/silabo/propuesta` | Docente | Multipart (`archivo`, ≤ 5 MB) → propuesta editable |
| `GET /api/auditoria` · `GET /api/auditoria/acciones` | Administrador | Bitácora con filtros `accion, origen, usuario, desde, hasta, page, pageSize` |

`POST /api/auditoria` ya **no se expone en el Gateway**: MS-B y MS-C escriben directo a MS-A con el token del usuario.

---

## 5. Manual de usuario

**Agente IA (M9)** — menú «Agente IA».
1. Elige la materia. El indicador superior muestra el modelo y si está disponible.
2. Escribe una pregunta o pulsa una sugerencia. Puedes nombrar a un estudiante («¿Cómo va Joel Herrera?»).
3. Lee los bloques: **Hechos** (calculados por el sistema), **Inferencias**, **Recomendaciones**, **Incertidumbre**. «Ver herramientas usadas» muestra qué consultó el agente.
4. Pestaña **Recomendaciones por estudiante**: selecciona a alguien → *Generar* → revisa la evidencia → edita (una línea por elemento) → *Guardar cambios* → *Aprobar*. Solo entonces se habilita **Enviar por correo**.

**Reportes (M10)** — *Generar reporte* crea uno nuevo con las cifras actuales; la vista previa es exactamente lo que trae el PDF; *Descargar PDF*.

**Estructura temática** — si la materia no tiene estructura: *Seleccionar PDF* del sílabo → revisa y corrige la propuesta → *Confirmar e importar*.

**Ficha del estudiante** — nueva sección «Tendencia y proyección» (con aviso de estimación y alerta temprana si corresponde).

**Auditoría (administrador)** — filtra por acción, origen, usuario y fechas.

### Escenarios del agente para la demostración

| # | Pregunta / acción | Resultado esperado |
|---|---|---|
| 1 | «¿Qué estudiantes están en riesgo y por qué?» | ≥ 4 herramientas; hechos con notas reales; nombres reales al docente |
| 2 | «¿Qué temas tienen peor desempeño?» | Incluye `obtener_desempeno_temas` |
| 3 | «¿Cómo va Joel Herrera?» | Agrega la ficha del estudiante; el modelo solo vio «E03» |
| 4 | Pregunta fuera del tema (mundial) | Responde con los datos del curso o dice que no hay información; no inventa |
| 5 | Apagar Ollama y repetir la 1 | Respuesta por reglas con advertencia «sin modelo» |
| 6 | docente2 consulta la materia de docente1 | 403 y `AGENTE_ACCESO_DENEGADO` en auditoría |
| 7 | Pregunta vacía | 400 con mensaje claro |
| 8 | Materia sin calificaciones sincronizadas | Mensaje de «sin datos»; no se inventa nada |

---

## 6. Matriz RF / RNF / reglas

| Requisito | Cómo se cubre |
|---|---|
| RF agente con herramientas | `Agente/Herramientas/*` (6 herramientas, parámetros validados) |
| RF recomendaciones editables y aprobadas | `RecomendacionService` (Borrador → Aprobada; editar reabre) |
| RF reporte PDF | `ReporteService` + `QuestPdfReporte` |
| RF auditoría | `AuditClient`, `AuditoriaService`, `AuditoriaController` |
| RN-07 solo mis materias | Validación por docente; 403 + auditoría |
| RN-09 cálculo por código, LLM explica | Hechos/incertidumbre por código; filtro de salida del modelo |
| RN-10 orientativo | Avisos en UI, PDF y correo |
| RN-11 LLM solo ve anónimos | `Anonimizador` |
| RNF seguridad | Sin tokens/contraseñas en la bitácora (`AuditSanitizer`); secretos solo en `.env`; correo solo bajo acción manual |
| RNF disponibilidad | Respaldo sin modelo; la auditoría y el correo nunca rompen la acción del usuario |
| RNF datos mínimos | El correo del estudiante no se replica: se consulta a MS-B al enviar |

---

## 7. Qué se verificó y qué no (transparencia)

**Verificado por el equipo en su entorno (Windows + Docker Desktop):**

| Verificado | Resultado |
|---|---|
| Compilación real y pruebas | `dotnet build` correcto y `dotnet test` en verde |
| Migración de MS-C | 4 tablas nuevas aplicadas en `DB_Analitica` |
| Agente con `qwen2.5:3b` en CPU | Responde (≈30 s con el modelo caliente); hechos, inferencias, recomendaciones e incertidumbre |
| Recomendaciones | Generar, editar (vuelve a borrador), aprobar, no regenerar una aprobada, envío solo de aprobadas |
| Correo | Llega a Mailpit; no se envía una recomendación en borrador |
| Reporte PDF | Vista previa y PDF con las mismas cifras del dashboard |
| Tendencia | Descendente (Joel Herrera, próxima nota 2,2) y estable; alerta temprana |
| Sílabo PDF | Propuesta editable, solo lectura si ya hay estructura; un PNG se rechaza |
| Auditoría | Filtros, paginación, sin tokens; accesos denegados registrados |
| Permisos | docente2: 403 en materias ajenas, 404 en inexistentes; reporte ajeno 403; auditoría 403 |

**Ajustes hechos al validar** (quedan como decisiones de diseño):

- El Gateway (YARP) corta a los 100 s por defecto: con Ollama en CPU se configuró `HttpRequest.ActivityTimeout` de 4 min en el clúster `analytics` y `LLM_TIMEOUT_SECONDS=180`.
- Cada pregunta del agente tiene un **enfoque** (estudiante, riesgo, temas, entregas, alertas, notas, resumen): solo se muestran los hechos que responden a la pregunta y solo esos se envían al modelo.
- Una pregunta ajena al curso se rechaza con ejemplos, sin consultar al modelo.
- Las afirmaciones del modelo que los hechos no respaldan (cifras que no calculó el sistema, la nota de otro estudiante, «sin datos» cuando todos tienen notas, tendencias no calculadas) se descartan y se avisa.
- El PDF muestra la hora de Ecuador; el texto del modelo se recorta en frases completas.
- El panel del sílabo PDF es siempre visible; con estructura existente la propuesta es de solo lectura (no se sobrescribe nada).

**Limitaciones conocidas:**

| Limitación | Detalle |
|---|---|
| Calidad del modelo pequeño | `qwen2.5:3b` escribe inferencias sencillas y puede confundir la nota ponderada con la de una actividad; las cifras y los hechos son siempre del código. Un modelo mayor (p. ej. `qwen2.5:7b`) se configura con `LLM_MODEL` sin cambiar el código |
| Tiempo de respuesta | Depende del equipo (CPU); con GPU o con Gemini/Groq es mucho menor |
| Lectura de sílabos escaneados | Un PDF que es solo imagen no tiene texto: no se puede leer (no hay OCR) |

Si algo falla al compilar o ejecutar, peguen el mensaje completo y se corrige.

---

## 8. Pruebas

| Proyecto | Archivo nuevo | Qué prueba |
|---|---|---|
| `EduMonitor.Analytics.Tests` | `AgenteLogicaTests.cs` | Anonimización, nombres compartidos, códigos inventados, planificador |
| | `TendenciaTests.cs` | Regresión, dirección, proyección acotada 0–10, alerta temprana |
| | `RecomendacionesLogicaTests.cs` | Diferencias por nivel, evidencia, lectura segura de la salida del modelo |
| | `SilaboYAuditoriaTests.cs` | Extracción por patrones, límites, ocultamiento de secretos, máscara de correo |
| `EduMonitor.Identity.Tests` | `AuditoriaServiceTests.cs` | Filtros, paginación, registro sin secretos, solicitudes inválidas |

Esperado: **90 previas + 59 nuevas = 149 pruebas correctas** (`dotnet test EduMonitor.sln`).

---

## 9. Reglas del equipo para este sprint

- Rama `feature/sprint-3` desde `develop`; Pull Request revisado por otro integrante; tag `sprint-3` al fusionar.
- Commits: `feat(HU-13): …`, `test(HU-15): …`, `docs(HU-18): …`.
- El `.env` nunca se sube; la clave de Gemini/Groq (si se usa) solo va en `.env`.
