// src/services/mockData.js

// Usuario de prueba para el login (temporal, hasta conectar con Persona 5)
export const usuarioMock = {
  usuario: "despachador1",
  contrasena: "1234",
  token: "jwt-de-mentira-123",
  nombre: "Juan Pérez",
  rol: "Despachador"
};

// Tickets de ejemplo con distintos estados, para probar todos los casos
export const ticketsMock = [
  {
    valido: true,
    estado: "Creado",
    ticket: {
      id: "COM-2026-000001",
      empleado: { codigo: "E001", nombre: "Juan Pérez" },
      vehiculo: { placa: "A123456", ficha: "V01" },
      departamento: "Logística",
      cantidadAutorizada: 20,
      tipoCombustible: "Gasolina Premium",
      fechaEmision: "2026-09-01",
      fechaVencimiento: "2026-09-30"
    },
    mensajeError: null
  },
  {
    valido: false,
    estado: "Vencido",
    ticket: null,
    mensajeError: "El ticket ya venció"
  },
  {
    valido: false,
    estado: "Consumido",
    ticket: null,
    mensajeError: "Este ticket ya fue utilizado"
  }
];

// Lista de tickets para la pantalla de consulta
export const listaTicketsMock = [
  {
    id: "COM-2026-000001",
    estado: "Pendiente",
    vehiculo: "A123456",
    cantidadAutorizada: 20,
    fechaVencimiento: "2026-09-30"
  },
  {
    id: "COM-2026-000002",
    estado: "Vencido",
    vehiculo: "B789012",
    cantidadAutorizada: 15,
    fechaVencimiento: "2026-09-10"
  }
];