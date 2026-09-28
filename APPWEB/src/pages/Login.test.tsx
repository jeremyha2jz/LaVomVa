/** @vitest-environment jsdom */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { Login } from './Login'

const auth = vi.hoisted(() => ({ login: vi.fn(), register: vi.fn() }))
vi.mock('../context/AppContext', () => ({ useApp: () => auth }))

afterEach(cleanup)
beforeEach(() => { auth.login.mockReset(); auth.register.mockReset() })

describe('pantalla de acceso', () => {
  it('explica los campos faltantes antes de llamar a la API', () => {
    render(<Login />)
    fireEvent.click(screen.getByRole('button', { name: 'Ingresar a la plataforma' }))
    expect(screen.getByText('Escribe tu usuario.')).toBeTruthy()
    expect(screen.getByText('Escribe tu contraseña.')).toBeTruthy()
    expect(auth.login).not.toHaveBeenCalled()
  })

  it('rechaza contraseñas distintas durante el registro', () => {
    render(<Login />)
    fireEvent.click(screen.getByRole('tab', { name: 'Crear cuenta' }))
    fireEvent.change(screen.getByLabelText('Nombre completo'), { target: { value: 'María Mora' } })
    fireEvent.change(screen.getByLabelText('Correo electrónico'), { target: { value: 'maria@example.com' } })
    fireEvent.change(screen.getByLabelText('Usuario'), { target: { value: 'mariamora' } })
    fireEvent.change(screen.getByLabelText('Contraseña'), { target: { value: 'clave-segura-123' } })
    fireEvent.change(screen.getByLabelText('Confirmar contraseña'), { target: { value: 'clave-segura-456' } })
    fireEvent.click(screen.getByRole('button', { name: 'Solicitar cuenta' }))
    expect(screen.getByText('Las contraseñas no coinciden.')).toBeTruthy()
    expect(auth.register).not.toHaveBeenCalled()
  })

  it('permite revelar y volver a ocultar la contraseña', () => {
    render(<Login />)
    const password = screen.getByLabelText('Contraseña') as HTMLInputElement
    expect(password.type).toBe('password')
    fireEvent.click(screen.getByRole('button', { name: 'Mostrar contraseña' }))
    expect(password.type).toBe('text')
    fireEvent.click(screen.getByRole('button', { name: 'Ocultar contraseña' }))
    expect(password.type).toBe('password')
  })

  it('informa que una cuenta registrada espera activación', async () => {
    auth.register.mockResolvedValue(undefined)
    render(<Login />)
    fireEvent.click(screen.getByRole('tab', { name: 'Crear cuenta' }))
    fireEvent.change(screen.getByLabelText('Nombre completo'), { target: { value: 'María Mora' } })
    fireEvent.change(screen.getByLabelText('Correo electrónico'), { target: { value: 'maria@example.com' } })
    fireEvent.change(screen.getByLabelText('Usuario'), { target: { value: 'mariamora' } })
    fireEvent.change(screen.getByLabelText('Contraseña'), { target: { value: 'clave-segura-123' } })
    fireEvent.change(screen.getByLabelText('Confirmar contraseña'), { target: { value: 'clave-segura-123' } })
    fireEvent.click(screen.getByRole('button', { name: 'Solicitar cuenta' }))
    await waitFor(() => expect(auth.register).toHaveBeenCalledWith('mariamora', 'maria@example.com', 'María Mora', 'clave-segura-123'))
    expect(await screen.findByRole('status')).toHaveProperty('textContent', expect.stringContaining('Un administrador debe activarla'))
    expect(screen.getByRole('tab', { name: 'Iniciar sesión' }).getAttribute('aria-selected')).toBe('true')
  })

  it('muestra el error de autenticación sin borrar el usuario', async () => {
    auth.login.mockRejectedValue(new Error('Usuario o contraseña incorrectos'))
    render(<Login />)
    fireEvent.change(screen.getByLabelText('Usuario'), { target: { value: 'mariamora' } })
    fireEvent.change(screen.getByLabelText('Contraseña'), { target: { value: 'clave' } })
    fireEvent.click(screen.getByRole('button', { name: 'Ingresar a la plataforma' }))
    expect((await screen.findByRole('alert')).textContent).toBe('Usuario o contraseña incorrectos')
    expect((screen.getByLabelText('Usuario') as HTMLInputElement).value).toBe('mariamora')
  })
})
