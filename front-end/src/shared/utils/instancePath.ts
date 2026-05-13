export function instanceBasePath(currentPathname: string, employeeId: string): string {
  if (currentPathname.startsWith("/my-employees/instances/")) {
    return `/my-employees/instances/${employeeId}`;
  }
  if (currentPathname.startsWith("/department-employees/instances/")) {
    return `/department-employees/instances/${employeeId}`;
  }
  return `/department-employees/instances/${employeeId}`;
}
