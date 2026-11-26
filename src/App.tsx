/**
 * @license
 * SPDX-License-Identifier: Apache-2.0
 */

import { NovaProvider } from './context/NovaContext';
import { WindowsDesktop } from './components/desktop/WindowsDesktop';

export default function App() {
  return (
    <NovaProvider>
      <WindowsDesktop />
    </NovaProvider>
  );
}
