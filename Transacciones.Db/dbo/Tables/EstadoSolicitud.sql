-- Catalogo de estados. Se lee en lugar de codificar los numeros en la
-- aplicacion: si manana se inserta un estado, el catalogo lo refleja sin
-- tocar codigo.
CREATE TABLE [dbo].[EstadoSolicitud]
(
    [Codigo]    [dbo].[CodigoEstado] NOT NULL,
    [Valor]     TINYINT             NOT NULL,
    [Descripcion] NVARCHAR(100)     NOT NULL,
    [EsTerminal] BIT                 NOT NULL
        CONSTRAINT [DF_EstadoSolicitud_EsTerminal] DEFAULT (0),

    CONSTRAINT [PK_EstadoSolicitud] PRIMARY KEY CLUSTERED ([Codigo]),
    CONSTRAINT [UQ_EstadoSolicitud_Valor] UNIQUE ([Valor])
);