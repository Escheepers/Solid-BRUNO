import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { Toast } from './shared/toast/toast';
import { TopAppBar } from './shared/top-app-bar/top-app-bar';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, TopAppBar, Toast],
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {}
