CREATE TABLE [dbo].[Solicitud]
(
    [Id]              UNIQUEIDENTIFIER NOT NULL,
    [Codigo]          NVARCHAR(30)       NOT NULL,
    [ClienteId]       UNIQUEIDENTIFIER   NOT NULL,

    -- DECIMAL(18,2): el monto es dinero. FLOAT no sirve (errores de redondeo
    -- binario) ni MONEY (es una Currency y se mezcla mal con otros proveedores).
    [Monto]           DECIMAL(18, 2)     NOT NULL,

    -- NVARCHAR y no CHAR: CHAR rellena con espacios y "PEN " != "PEN".
    [Moneda]          NVARCHAR(3)        NOT NULL
        CONSTRAINT [DF_Solicitud_Moneda] DEFAULT (N'PEN'),

    -- TINYINT cabe de sobra para 6 estados y ocupa 1 byte frente a 4.
    [Estado]          TINYINT            NOT NULL
        CONSTRAINT [DF_Solicitud_Estado] DEFAULT (1),

    [Observacion]     NVARCHAR(500)      NULL,
    [MotivoRechazo]   NVARCHAR(500)      NULL,
    [FechaSolicitud]  DATETIME2(0)       NOT NULL,
    [FechaVencimiento] DATETIME2(0)      NOT NULL,
    [FechaAprobacion] DATETIME2(0)       NULL,
    [FechaRechazo]    DATETIME2(0)       NULL,

    [FechaCreacion]   DATETIME2(0)       NOT NULL
        CONSTRAINT [DF_Solicitud_FechaCreacion] DEFAULT (SYSUTCDATETIME()),
    [CreadoPor]       NVARCHAR(100)      NOT NULL
        CONSTRAINT [DF_Solicitud_CreadoPor] DEFAULT (SUSER_SNAME()),
    [FechaModificacion] DATETIME2(0)     NULL,
    [ModificadoPor]   NVARCHAR(100)      NULL,

    CONSTRAINT [PK_Solicitud] PRIMARY KEY CLUSTERED ([Id]),

    -- Los CHECK son la ultima linea de defensa. La aplicacion ya valida esto,
    -- pero la base no deberia aceptar un importe negativo aunque alguien
    -- escriba por otra via (un script, una herramienta, un bug futuro).
    CONSTRAINT [CK_Solicitud_Monto]  CHECK ([Monto] > 0),
    CONSTRAINT [CK_Solicitud_Estado]  CHECK ([Estado] BETWEEN 0 AND 5),
    CONSTRAINT [CK_Solicitud_Registro] CHECK ([FechaVencimiento] > [FechaSolicitud]),
    CONSTRAINT [CK_Solicitud_Rechazo] CHECK ([Estado] <> 3 OR [MotivoRechazo] IS NOT NULL)
);

-- El codigo es la clave que el usuario ve y teclea: tiene que ser unico.
CREATE UNIQUE INDEX [UX_Solicitud_Codigo] ON [dbo].[Solicitud] ([Codigo]);

-- Los tres indices siguientes cubren las consultas del listado filtrado.
CREATE INDEX [IX_Solicitud_ClienteId] ON [dbo].[Solicitud] ([ClienteId]);
CREATE INDEX [IX_Solicitud_Estado]    ON [dbo].[Solicitud] ([Estado]);
CREATE INDEX [IX_Solicitud_Fecha]     ON [dbo].[Solicitud] ([FechaSolicitud] DESC);

-- Indice COMPUESTO y FILTRADO. El job de vencimiento busca por FechaVencimiento
-- sobre lo que aun no esta resuelto (0 Borrador, 1 Registrada), asi que el
-- indice cubre exactamente lo que ese job consulta.
--
-- El filtro se escribe en FORMA POSITIVA (IN) y no como "NOT IN (3,4,5)":
-- el predicado de un indice filtrado no admite NOT en este motor, y ademas
-- listar los estados que SÍ se consultan documenta mejor la intencion que
-- enumerar los que se descartan.
CREATE INDEX [IX_Solicitud_Vencimiento_NoTerminales]
    ON [dbo].[Solicitud] ([FechaVencimiento], [Estado])
    WHERE [Estado] IN (0, 1);